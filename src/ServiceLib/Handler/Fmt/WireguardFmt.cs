namespace ServiceLib.Handler.Fmt;

public class WireguardFmt : BaseFmt
{
    public static ProfileItem? Resolve(string str, out string msg)
    {
        msg = ResUI.ConfigurationFormatIncorrect;

        ProfileItem item = new()
        {
            ConfigType = EConfigType.WireGuard
        };

        var url = Utils.TryUri(str);
        if (url == null)
        {
            return null;
        }

        item.Address = url.IdnHost;
        item.Port = url.Port;
        item.Remarks = url.GetComponents(UriComponents.Fragment, UriFormat.Unescaped);
        item.Password = Utils.UrlDecode(url.UserInfo);

        var query = Utils.ParseQueryString(url.Query);

        item.SetProtocolExtra(item.GetProtocolExtra() with
        {
            WgPublicKey = GetQueryDecoded(query, "publickey"),
            WgPresharedKey = GetQueryDecoded(query, "presharedkey"),
            WgReserved = GetQueryDecoded(query, "reserved"),
            WgInterfaceAddress = GetQueryDecoded(query, "address"),
            WgMtu = int.TryParse(GetQueryDecoded(query, "mtu"), out var mtuVal) ? mtuVal : null,
        });

        return item;
    }

    public static string? ToUri(ProfileItem? item)
    {
        if (item == null)
        {
            return null;
        }

        var remark = string.Empty;
        if (item.Remarks.IsNotEmpty())
        {
            remark = "#" + Utils.UrlEncode(item.Remarks);
        }

        var protoExtra = item.GetProtocolExtra();
        var dicQuery = new Dictionary<string, string>();
        if (!protoExtra.WgPublicKey.IsNullOrEmpty())
        {
            dicQuery.Add("publickey", Utils.UrlEncode(protoExtra.WgPublicKey));
        }
        if (!protoExtra.WgPresharedKey.IsNullOrEmpty())
        {
            dicQuery.Add("presharedkey", Utils.UrlEncode(protoExtra.WgPresharedKey));
        }
        if (!protoExtra.WgReserved.IsNullOrEmpty())
        {
            dicQuery.Add("reserved", Utils.UrlEncode(protoExtra.WgReserved));
        }
        if (!protoExtra.WgInterfaceAddress.IsNullOrEmpty())
        {
            dicQuery.Add("address", Utils.UrlEncode(protoExtra.WgInterfaceAddress));
        }
        if (protoExtra.WgMtu > 0)
        {
            dicQuery.Add("mtu", protoExtra.WgMtu.ToString());
        }
        return ToUri(EConfigType.WireGuard, item.Address, item.Port, item.Password, dicQuery, remark);
    }

    /// <summary>The Cloudflare WARP server key, shared by every WARP config.</summary>
    public const string WarpPublicKey = "bmXOC+F1FxEMF9dyiK2H5/1SUtzH0JuVo51h2wPfgyo=";

    private const int AwgMaxJunkCount = 128;
    private const int AwgMaxJunkSize = 1280;

    public static List<ProfileItem>? ResolveConfig(string strData)
    {
        return ResolveConfig(strData, out _);
    }

    /// <summary>
    /// Reads a WireGuard or AmneziaWG .conf. <paramref name="unsupportedAmnezia"/> is true when
    /// the config needs AmneziaWG on the server side too (custom H1-H4 headers or S1-S4
    /// padding) or carries decoys that cannot be read: such a config is refused, because a plain
    /// WireGuard core would import it but never connect. Jc/Jmin/Jmax and I1-I5 alone are sent
    /// by the client only, before each handshake, and are kept for the local relay.
    /// </summary>
    public static List<ProfileItem>? ResolveConfig(string strData, out bool unsupportedAmnezia)
    {
        unsupportedAmnezia = false;
        var interfaceDic = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var peerDicList = new List<Dictionary<string, string>>();
        var currentDicRef = interfaceDic;
        using (var reader = new StringReader(strData))
        {
            while (reader.ReadLine() is { } line)
            {
                if (line.IsNullOrEmpty())
                {
                    continue;
                }

                var trimmedLine = line.Trim();

                if (trimmedLine.Equals("[Interface]", StringComparison.OrdinalIgnoreCase))
                {
                    currentDicRef = interfaceDic;
                    continue;
                }
                if (trimmedLine.Equals("[Peer]", StringComparison.OrdinalIgnoreCase))
                {
                    var peerDic = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    peerDicList.Add(peerDic);
                    currentDicRef = peerDic;
                    continue;
                }

                if (trimmedLine.StartsWith('[') || trimmedLine.StartsWith('#') || trimmedLine.StartsWith(';'))
                {
                    continue;
                }

                var idx = line.IndexOf('=');
                if (idx <= 0)
                {
                    continue;
                }

                var key = line[..idx].Trim();
                var value = line[(idx + 1)..].Trim();
                var commentPos = value.IndexOfAny(['#', ';']);
                if (commentPos >= 0)
                {
                    value = value[..commentPos].TrimEnd();
                }

                currentDicRef[key] = value;
            }
        }

        if (!interfaceDic.TryGetValue("PrivateKey", out var privateKey) || privateKey.IsNullOrEmpty())
        {
            return null;
        }

        if (!TryResolveAmnezia(interfaceDic, out var awgJc, out var awgJmin, out var awgJmax, out var awgSpecialPackets))
        {
            unsupportedAmnezia = true;
            return null;
        }

        var wgMtu = interfaceDic.TryGetValue("MTU", out var mtuStr) && int.TryParse(mtuStr, out var mtuVal) ? mtuVal : 0;
        var wgInterfaceAddress = interfaceDic.TryGetValue("Address", out var interfaceAddress) ? interfaceAddress : string.Empty;

        var index = 0;
        var resultList = new List<ProfileItem>();

        foreach (var peerDic in peerDicList)
        {
            if (!peerDic.TryGetValue("Endpoint", out var endpoint) || endpoint.IsNullOrEmpty())
            {
                continue;
            }

            if (!TryParseEndpoint(endpoint, out var peerAddress, out var peerPort))
            {
                continue;
            }

            var protoExtra = new ProtocolExtraItem
            {
                WgPublicKey = (peerDic.TryGetValue("PublicKey", out var publicKey) ? publicKey : string.Empty).NullIfEmpty(),
                WgPresharedKey = (peerDic.TryGetValue("PresharedKey", out var presharedKey) ? presharedKey : string.Empty).NullIfEmpty(),
                WgInterfaceAddress = wgInterfaceAddress,
                WgReserved = (peerDic.TryGetValue("Reserved", out var reserved) ? reserved : string.Empty).NullIfEmpty(),
                WgMtu = wgMtu > 0 ? wgMtu : null,
                AwgJc = awgJc,
                AwgJmin = awgJmin,
                AwgJmax = awgJmax,
                AwgSpecialPackets = awgSpecialPackets,
            };

            var isWarp = protoExtra.WgPublicKey == WarpPublicKey;
            var item = new ProfileItem
            {
                Remarks = isWarp
                    ? (peerDicList.Count > 1 ? $"Cloudflare WARP {index + 1}" : "Cloudflare WARP")
                    : $"{nameof(EConfigType.WireGuard)} Peer {index + 1}",
                ConfigType = EConfigType.WireGuard,
                Address = peerAddress,
                Port = peerPort,
                Password = privateKey,
            };
            item.SetProtocolExtra(protoExtra);
            resultList.Add(item);

            index += 1;
        }

        return resultList;
    }

    /// <summary>
    /// Takes the AmneziaWG keys of an [Interface] section. False when one of them asks for
    /// something only an AmneziaWG server understands, or cannot be read.
    /// </summary>
    private static bool TryResolveAmnezia(Dictionary<string, string> interfaceDic,
        out int? jc, out int? jmin, out int? jmax, out List<string>? specialPackets)
    {
        jc = null;
        jmin = null;
        jmax = null;
        specialPackets = null;

        // H1-H4 are the four WireGuard message types; anything but 1-4 changes the handshake itself.
        for (var i = 1; i <= 4; i++)
        {
            if (interfaceDic.TryGetValue($"H{i}", out var header) && header.IsNotEmpty()
                && header != i.ToString() && header != $"{i}-{i}")
            {
                return false;
            }
        }
        // S1-S4 pad the WireGuard messages; the server has to strip them.
        for (var i = 1; i <= 4; i++)
        {
            if (interfaceDic.TryGetValue($"S{i}", out var padding) && padding.IsNotEmpty() && padding != "0")
            {
                return false;
            }
        }

        if (interfaceDic.TryGetValue("Jc", out var jcText) && jcText.IsNotEmpty())
        {
            if (!int.TryParse(jcText, out var count) || count is < 0 or > AwgMaxJunkCount)
            {
                return false;
            }
            if (count > 0)
            {
                if (!int.TryParse(interfaceDic.GetValueOrDefault("Jmin"), out var min)
                    || !int.TryParse(interfaceDic.GetValueOrDefault("Jmax"), out var max)
                    || min < 1 || max < min || max > AwgMaxJunkSize)
                {
                    return false;
                }
                jc = count;
                jmin = min;
                jmax = max;
            }
        }

        for (var i = 1; i <= 5; i++)
        {
            if (!interfaceDic.TryGetValue($"I{i}", out var spec) || spec.IsNullOrEmpty())
            {
                continue;
            }
            if (!AwgSignature.TryParse(spec, out _))
            {
                return false;
            }
            (specialPackets ??= []).Add(spec);
        }

        return true;
    }

    private static bool TryParseEndpoint(string endpoint, out string address, out int port)
    {
        address = string.Empty;
        port = 2408;

        var trimmedEndpoint = endpoint.Trim();
        if (trimmedEndpoint.IsNullOrEmpty())
        {
            return false;
        }

        if (trimmedEndpoint[0] == '[')
        {
            var closeIndex = trimmedEndpoint.IndexOf(']');
            if (closeIndex <= 1)
            {
                return false;
            }

            address = trimmedEndpoint[1..closeIndex].Trim();
            var portIndex = closeIndex + 1;
            if (portIndex < trimmedEndpoint.Length && trimmedEndpoint[portIndex] == ':' &&
                int.TryParse(trimmedEndpoint[(portIndex + 1)..].Trim(), out var bracketedPort) && bracketedPort is > 0 and <= 65535)
            {
                port = bracketedPort;
            }

            return address.IsNotEmpty();
        }

        var lastColonIndex = trimmedEndpoint.LastIndexOf(':');
        if (lastColonIndex <= 0)
        {
            address = trimmedEndpoint;
            return true;
        }

        address = trimmedEndpoint[..lastColonIndex].Trim();
        var portText = trimmedEndpoint[(lastColonIndex + 1)..].Trim();
        if (address.IsNullOrEmpty())
        {
            return false;
        }

        if (int.TryParse(portText, out var parsedPortValue) && parsedPortValue is > 0 and <= 65535)
        {
            port = parsedPortValue;
            return true;
        }

        address = trimmedEndpoint;
        return true;
    }
}
