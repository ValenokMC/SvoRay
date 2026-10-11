namespace ServiceLib.Common;

/// <summary>
/// Routing rules a subscription hands out in the Happ format: which sites go through the VPN,
/// which go direct and which are blocked.
/// </summary>
/// <remarks>
/// The server sends the profile to every client in the <c>routing</c> response header, as
/// <c>happ://routing/onadd/&lt;base64 JSON&gt;</c>. Happ applies it; until now SvoRay read only
/// <c>Support-Url</c> from the same response, so a PC sent everything through the tunnel while a
/// phone on the same subscription kept Russian sites and games direct.
/// </remarks>
public sealed class SvoRayServerRoutingProfile
{
    public string Name { get; set; } = string.Empty;

    /// <summary>Order in which the three groups are matched, e.g. <c>block-proxy-direct</c>.</summary>
    public string RouteOrder { get; set; } = SvoRayServerRouting.DefaultRouteOrder;

    /// <summary>Xray domain strategy, only when it is one the core understands.</summary>
    public string? DomainStrategy { get; set; }

    public List<string> DirectSites { get; set; } = [];
    public List<string> DirectIp { get; set; } = [];
    public List<string> ProxySites { get; set; } = [];
    public List<string> ProxyIp { get; set; } = [];
    public List<string> BlockSites { get; set; } = [];
    public List<string> BlockIp { get; set; } = [];

    /// <summary>Geo files the lists were written against; their categories are not in the stock files.</summary>
    public string? GeositeUrl { get; set; }
    public string? GeoipUrl { get; set; }

    public bool HasRules =>
        DirectSites.Count + DirectIp.Count + ProxySites.Count + ProxyIp.Count + BlockSites.Count + BlockIp.Count > 0;
}

public static class SvoRayServerRouting
{
    public const string HeaderName = "routing";
    public const string EnableHeaderName = "routing-enable";
    public const string DefaultRouteOrder = "block-proxy-direct";

    /// <summary>
    /// The profile's geo files live next to the stock ones under their own names and are reached
    /// with Xray's <c>ext:</c> syntax. Replacing <c>geosite.dat</c> instead would break every other
    /// rule set that relies on the stock categories.
    /// </summary>
    public const string GeoSiteFileName = "svoray_geosite.dat";
    public const string GeoIpFileName = "svoray_geoip.dat";

    private const int MaxHeaderLength = 256 * 1024;
    private const int MaxEntries = 5000;
    private const int MaxEntryLength = 256;
    private const long MaxGeoFileBytes = 64L * 1024 * 1024;

    private static readonly string[] Schemes = ["happ://routing/onadd/", "happ://routing/add/"];
    private static readonly string[] Groups = ["block", "proxy", "direct"];

    /// <summary>
    /// What to store for a subscription after an update: the validated profile as JSON, or null
    /// when the server sent none, switched it off or sent something unusable.
    /// </summary>
    public static string? FromHeaders(string? routing, string? enable)
    {
        var flag = enable?.Trim();
        if (flag.IsNotEmpty() && (flag!.Equals("false", StringComparison.OrdinalIgnoreCase) || flag == "0"))
        {
            return null;
        }

        var profile = ParseHeader(routing);
        return profile is null ? null : JsonUtils.Serialize(profile, false);
    }

    public static SvoRayServerRoutingProfile? ParseHeader(string? value)
    {
        var text = value?.Trim();
        if (text.IsNullOrEmpty() || text!.Length > MaxHeaderLength)
        {
            return null;
        }

        var scheme = Schemes.FirstOrDefault(s => text.StartsWith(s, StringComparison.OrdinalIgnoreCase));
        if (scheme is null)
        {
            return null;
        }

        var json = DecodeBase64(text[scheme.Length..]);
        return json is null ? null : Parse(json);
    }

    /// <summary>Reads a profile, either fresh from the server or as stored on the subscription.</summary>
    public static SvoRayServerRoutingProfile? Parse(string? json)
    {
        if (json.IsNullOrEmpty())
        {
            return null;
        }

        JsonObject? root;
        try
        {
            root = JsonNode.Parse(json!) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
        if (root is null)
        {
            return null;
        }

        var profile = new SvoRayServerRoutingProfile
        {
            Name = Text(root, "Name") ?? string.Empty,
            RouteOrder = NormalizeRouteOrder(Text(root, "RouteOrder")),
            DomainStrategy = Global.DomainStrategies.FirstOrDefault(s =>
                s.Equals(Text(root, "DomainStrategy"), StringComparison.OrdinalIgnoreCase)),
            DirectSites = Entries(root, "DirectSites"),
            DirectIp = Entries(root, "DirectIp"),
            ProxySites = Entries(root, "ProxySites"),
            ProxyIp = Entries(root, "ProxyIp"),
            BlockSites = Entries(root, "BlockSites"),
            BlockIp = Entries(root, "BlockIp"),
            GeositeUrl = HttpsUrl(Text(root, "Geositeurl") ?? Text(root, "GeositeUrl")),
            GeoipUrl = HttpsUrl(Text(root, "Geoipurl") ?? Text(root, "GeoipUrl")),
        };
        if (profile.Name.Length > MaxEntryLength)
        {
            profile.Name = profile.Name[..MaxEntryLength];
        }

        return profile.HasRules ? profile : null;
    }

    /// <summary>
    /// Rewrites <c>geosite:</c>/<c>geoip:</c> entries to the profile's own file, keeping only
    /// categories that file actually contains. Xray refuses to start on a single unknown
    /// category, so dropping one is the difference between a missing rule and no connection.
    /// </summary>
    /// <param name="entries">Entries of one list from the profile.</param>
    /// <param name="prefix"><c>geosite:</c> or <c>geoip:</c>.</param>
    /// <param name="fileName">
    /// The profile's geo file for that prefix, or null when the profile names none and its
    /// categories are checked against the stock file and kept as they are.
    /// </param>
    /// <param name="tags">Categories present in the file, or null when the file is unavailable.</param>
    /// <param name="dropped">Entries that were left out, for the log.</param>
    public static List<string> RewriteGeo(IEnumerable<string> entries, string prefix, string? fileName,
        IReadOnlySet<string>? tags, List<string> dropped)
    {
        var result = new List<string>();
        foreach (var entry in entries)
        {
            // A server must not point the core at an arbitrary local file.
            if (entry.StartsWith("ext:", StringComparison.OrdinalIgnoreCase))
            {
                dropped.Add(entry);
                continue;
            }

            if (!entry.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                result.Add(entry);
                continue;
            }

            var category = entry[prefix.Length..];
            var bare = category.Split('@')[0];
            if (tags is not null && bare.IsNotEmpty() && tags.Contains(bare))
            {
                result.Add(fileName is null ? entry : $"ext:{fileName}:{category}");
            }
            else
            {
                dropped.Add(entry);
            }
        }
        return result;
    }

    /// <summary>
    /// Category names inside a geosite/geoip file: both are a protobuf list whose entries start
    /// with the category as field 1. Returns an empty set for a missing or malformed file.
    /// </summary>
    public static HashSet<string> ReadGeoTags(string path)
    {
        var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (!SvoRayPaths.IsLocalPath(path) || !File.Exists(path))
            {
                return tags;
            }
            var data = File.ReadAllBytes(path);
            var i = 0;
            while (i < data.Length)
            {
                var key = ReadVarint(data, ref i);
                if (key != 0x0A)
                {
                    tags.Clear();
                    return tags;
                }
                var length = (int)ReadVarint(data, ref i);
                var end = checked(i + length);
                if (length < 0 || end > data.Length)
                {
                    tags.Clear();
                    return tags;
                }

                var j = i;
                if (ReadVarint(data, ref j) == 0x0A)
                {
                    var nameLength = (int)ReadVarint(data, ref j);
                    if (nameLength > 0 && j + nameLength <= end)
                    {
                        tags.Add(Encoding.UTF8.GetString(data, j, nameLength));
                    }
                }
                i = end;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OverflowException
                                       or IndexOutOfRangeException)
        {
            tags.Clear();
        }
        return tags;
    }

    /// <summary>
    /// Downloads the profile's geo files when they are missing or the server switched to other
    /// ones. A failed download keeps whatever was there before; the rules then fall back to the
    /// categories that file has, or lose the geo entries altogether.
    /// </summary>
    public static async Task EnsureGeoFilesAsync(SvoRayServerRoutingProfile profile)
    {
        await EnsureGeoFileAsync(profile.GeositeUrl, Utils.GetBinPath(GeoSiteFileName));
        await EnsureGeoFileAsync(profile.GeoipUrl, Utils.GetBinPath(GeoIpFileName));
    }

    private static async Task EnsureGeoFileAsync(string? url, string path)
    {
        // The files live in SvoRay's own bin folder; anything else is refused before it is written.
        if (url.IsNullOrEmpty() || !SvoRayPaths.IsLocalPath(path))
        {
            return;
        }

        var marker = path + ".url";
        try
        {
            if (File.Exists(path) && File.Exists(marker)
                && (await File.ReadAllTextAsync(marker)).Trim() == url
                && ReadGeoTags(path).Count > 0)
            {
                return;
            }
        }
        catch (IOException)
        {
        }

        // Direct first: at connect time the tunnel is usually not up yet. Through the local
        // proxy second, for a host that is blocked where the user is.
        foreach (var viaProxy in new[] { false, true })
        {
            try
            {
                var data = await DownloadAsync(url!, viaProxy);
                if (data is null)
                {
                    continue;
                }

                var temp = path + ".tmp";
                await File.WriteAllBytesAsync(temp, data);
                if (ReadGeoTags(temp).Count == 0)
                {
                    File.Delete(temp);
                    continue;
                }
                File.Move(temp, path, true);
                await File.WriteAllTextAsync(marker, url);
                return;
            }
            catch (Exception ex)
            {
                Logging.SaveLog("SvoRay geo file", ex);
            }
        }
    }

    private static async Task<byte[]?> DownloadAsync(string url, bool viaProxy)
    {
        var handler = new SocketsHttpHandler { AutomaticDecompression = DecompressionMethods.All };
        if (viaProxy)
        {
            var port = AppManager.Instance.GetLocalPort(EInboundProtocol.socks);
            if (!await IsListeningAsync(port))
            {
                return null;
            }
            handler.Proxy = new WebProxy($"socks5://{Global.Loopback}:{port}");
            handler.UseProxy = true;
        }
        else
        {
            handler.UseProxy = false;
        }

        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.TryParseAdd(Utils.GetVersion(false).Replace(' ', '/'));
        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > MaxGeoFileBytes)
        {
            return null;
        }
        var data = await response.Content.ReadAsByteArrayAsync();
        return data.Length is > 0 and <= (int)MaxGeoFileBytes ? data : null;
    }

    private static async Task<bool> IsListeningAsync(int port)
    {
        try
        {
            using var client = new TcpClient();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            await client.ConnectAsync(Global.Loopback, port, cts.Token);
            return true;
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException)
        {
            return false;
        }
    }

    private static ulong ReadVarint(byte[] data, ref int i)
    {
        ulong result = 0;
        for (var shift = 0; shift < 64; shift += 7)
        {
            var b = data[i++];
            result |= (ulong)(b & 0x7F) << shift;
            if (b < 0x80)
            {
                return result;
            }
        }
        throw new OverflowException("varint is too long");
    }

    private static string? DecodeBase64(string text)
    {
        var value = text.Trim().Replace('-', '+').Replace('_', '/');
        value = value.PadRight(value.Length + (4 - value.Length % 4) % 4, '=');
        try
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(value));
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static JsonNode? Field(JsonObject root, string name)
    {
        foreach (var (key, node) in root)
        {
            if (key.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return node;
            }
        }
        return null;
    }

    private static string? Text(JsonObject root, string name)
    {
        return Field(root, name) is JsonValue value && value.TryGetValue<string>(out var text)
            ? text.Trim()
            : null;
    }

    private static List<string> Entries(JsonObject root, string name)
    {
        if (Field(root, name) is not JsonArray array)
        {
            return [];
        }

        return array
            .Select(node => node is JsonValue v && v.TryGetValue<string>(out var s) ? s.Trim() : null)
            .Where(IsAcceptableEntry)
            .Select(s => s!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxEntries)
            .ToList();
    }

    /// <summary>
    /// A matcher, not free text: no spaces, quotes or control characters that could change the
    /// meaning of the generated core config.
    /// </summary>
    private static bool IsAcceptableEntry(string? entry)
    {
        return entry.IsNotEmpty()
               && entry!.Length <= MaxEntryLength
               && entry.All(c => c is > ' ' and < '\u007F' and not ('"' or '\'' or '\\' or '`' or ','));
    }

    private static string? HttpsUrl(string? value)
    {
        return value.IsNotEmpty() && value!.Length <= 2048
               && Uri.TryCreate(value, UriKind.Absolute, out var uri)
               && uri.Scheme == Uri.UriSchemeHttps && uri.Host.IsNotEmpty()
            ? uri.AbsoluteUri
            : null;
    }

    private static string NormalizeRouteOrder(string? value)
    {
        var parts = value?.ToLowerInvariant().Split('-', StringSplitOptions.TrimEntries) ?? [];
        return parts.Length == Groups.Length && Groups.All(parts.Contains)
            ? string.Join('-', parts)
            : DefaultRouteOrder;
    }
}
