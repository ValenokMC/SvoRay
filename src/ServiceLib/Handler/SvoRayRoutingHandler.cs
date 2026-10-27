namespace ServiceLib.Handler;

/// <summary>
/// Builds the routing profile the simple screen owns.
/// </summary>
/// <remarks>
/// The profile is regenerated from scratch every time, so the domain list in the simple screen
/// is the only thing that decides what it contains - editing it in the advanced routing window
/// would be overwritten on the next connect.
/// <para>
/// Nothing beyond these rules is needed to keep DNS honest: the core config services read the
/// same rule set and send domains routed to <c>direct</c> to the direct DNS server. Without that
/// a bypassed domain would leave the VPN but still be resolved through it, and come back with an
/// address picked for the wrong country.
/// </para>
/// </remarks>
public static class SvoRayRoutingHandler
{
    /// <summary>Name of the generated routing profile; visible in advanced mode.</summary>
    public const string RoutingRemarks = "SvoRay";

    /// <summary>
    /// Starting set for users in Russia: sites that refuse or degrade on a foreign address.
    /// </summary>
    /// <remarks>
    /// Each entry matches its subdomains, so only second-level names are listed. Static and
    /// media hosts are listed separately wherever they sit on a domain of their own - a bank or
    /// a shop whose pages load direct while its images still go through the tunnel is the most
    /// common way this ends up half-broken.
    /// <para>
    /// It is a starting point, not a guarantee: these hosts change hands and names over time,
    /// and an entry that no longer resolves to anything simply never matches.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<string> RussiaPreset { get; } =
    [
        // State services. gov.ru covers every federal service under it, so nalog.gov.ru,
        // rosreestr.gov.ru, fssp.gov.ru and the rest need no separate entry.
        "gosuslugi.ru",
        "gu-st.ru",
        "xn--90aijkdmaud0d.xn--p1ai", // gosuslugi.rf in punycode.
        "gov.ru",
        "nalog.ru",
        "gibdd.ru",
        "mos.ru",
        "mosreg.ru",
        "gorzdrav.spb.ru",
        "pochta.ru",
        "russianpost.ru",
        "rzd.ru",
        "rzd-bonus.ru",
        "avtodor-tr.ru",
        "onelya.ru",
        "consultant.ru",

        // Utilities and insurance, which are among the strictest about foreign addresses
        "mosenergosbyt.ru",
        "pesc.ru",
        "reso.ru",
        "dzvr.ru",
        "bkvet.ru",

        // Banks and payments
        "sber.ru",
        "sberbank.ru",
        "tbank.ru",
        "tinkoff.ru",
        "vtb.ru",
        "alfabank.ru",
        "alfabank.st",
        "gazprombank.ru",
        "raiffeisen.ru",
        "psbank.ru",
        "open.ru",
        "sovcombank.ru",
        "rshb.ru",
        "mkb.ru",
        "pochtabank.ru",
        "nspk.ru",
        "mironline.ru",

        // Yandex
        "yandex.ru",
        "ya.ru",
        "yandex.net",
        "yastatic.net",
        "yandexcloud.net",
        "kinopoisk.ru",
        "dzen.ru",
        "auto.ru",

        // VK, Mail.ru, OK
        "vk.com",
        "vk.ru",
        "vk.me",
        "userapi.com",
        "vk-cdn.net",
        "vkuservideo.net",
        "vkvideo.ru",
        "ok.ru",
        "mycdn.me",
        "mail.ru",
        "imgsmail.ru",

        // Shops and services
        "ozon.ru",
        "ozone.ru",
        "ozonusercontent.com",
        "wildberries.ru",
        "wbbasket.ru",
        "wbstatic.net",
        "avito.ru",
        "avito.st",
        "dns-shop.ru",
        "citilink.ru",
        "mvideo.ru",
        "magnit.ru",
        "leroymerlin.ru",
        "lemanapro.ru",
        "emex.ru",
        "2gis.ru",
        "hh.ru",
        "cian.ru",
        "domclick.ru",
        "drom.ru",

        // Widgets and counters that hang a page load when they cannot be reached
        "bitrix.info",
        "counter.yadro.ru",

        // Mobile operators
        "mts.ru",
        "beeline.ru",
        "megafon.ru",
        "tele2.ru",

        // Video services that geo-block foreign addresses
        "rutube.ru",
        "ivi.ru",
        "okko.tv",
        "premier.one",
        "wink.ru"
    ];

    /// <summary>
    /// Community-maintained list of Russian resources that answer only to Russian addresses.
    /// The built-in preset is a snapshot plus the banks, social networks and marketplaces this
    /// list does not carry; downloading it keeps the shared part current without a new release.
    /// </summary>
    private const string RussiaListUrl =
        "https://raw.githubusercontent.com/itdoginfo/allow-domains/main/Russia/outside-raw.lst";

    /// <summary>
    /// Fetches the maintained list. Returns an empty list when it cannot be reached - the caller
    /// already has the built-in preset, so a failed refresh is not a failed operation.
    /// </summary>
    /// <remarks>
    /// The proxy is tried first: the host is commonly unreachable from inside Russia, which is
    /// exactly where this list is wanted, and going through a running tunnel is the attempt more
    /// likely to succeed. When no core is running that fails at once and the direct attempt runs.
    /// </remarks>
    public static async Task<List<string>> DownloadRussiaListAsync()
    {
        var downloadService = new DownloadService();
        foreach (var viaProxy in new[] { true, false })
        {
            try
            {
                var content = await downloadService.TryDownloadString(RussiaListUrl, viaProxy, string.Empty);
                var domains = ParseDomains(content);
                if (domains.Count > 0)
                {
                    return domains;
                }
            }
            catch (Exception ex)
            {
                Logging.SaveLog("SvoRay domain list", ex);
            }
        }

        return [];
    }

    /// <summary>
    /// Splits pasted text into domains. Anything a list can be separated by is accepted, so a
    /// column copied out of a document and a comma-separated line both work.
    /// </summary>
    public static List<string> ParseDomains(string? text)
    {
        if (text.IsNullOrEmpty())
        {
            return [];
        }

        return text!
            .Split(['\n', '\r', ',', ';', ' ', '\t'], StringSplitOptions.RemoveEmptyEntries)
            .Select(NormalizeDomain)
            .Where(domain => domain.IsNotEmpty())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Rebuilds the profile and makes it the active one. Saving the config is left to the caller,
    /// which usually has more to store than the profile id.
    /// </summary>
    public static async Task ApplyAsync(Config config)
    {
        var items = await AppManager.Instance.RoutingItems() ?? [];
        var item = items.FirstOrDefault(t => t.Id == config.SvoRayItem.RoutingId)
            ?? items.FirstOrDefault(t => t.Remarks == RoutingRemarks)
            ?? new RoutingItem { Remarks = RoutingRemarks, Sort = items.Count + 1 };
        item.Remarks = RoutingRemarks;

        var inputs = await CollectInputsAsync(config);
        var rules = BuildRules(config.SvoRayItem, inputs);
        item.DomainStrategy = inputs.ServerProfile?.DomainStrategy ?? item.DomainStrategy;
        if (await ConfigHandler.AddBatchRoutingRules(item, JsonUtils.Serialize(rules, false)) != 0)
        {
            return;
        }

        config.SvoRayItem.RoutingId = item.Id;
        await ConfigHandler.SetDefaultRouting(config, item);
    }

    /// <summary>
    /// What the rules depend on beyond the user's own settings: the profile the subscription of
    /// the selected server sends, the categories its geo files really contain, and where games
    /// are installed on this PC. Gathered at connect time, so a new library or an updated
    /// profile takes effect on the next connect without anything else to do.
    /// </summary>
    public static async Task<SvoRayRoutingInputs> CollectInputsAsync(Config config)
    {
        var settings = config.SvoRayItem;
        SvoRayServerRoutingProfile? profile = null;
        IReadOnlySet<string>? siteTags = null;
        IReadOnlySet<string>? ipTags = null;

        if (settings.UseServerRouting)
        {
            profile = await FindServerProfileAsync(config);
            if (profile is not null)
            {
                await SvoRayServerRouting.EnsureGeoFilesAsync(profile);
                siteTags = ReadTags(profile.GeositeUrl is null ? "geosite.dat" : SvoRayServerRouting.GeoSiteFileName);
                ipTags = ReadTags(profile.GeoipUrl is null ? "geoip.dat" : SvoRayServerRouting.GeoIpFileName);
            }
        }

        return new SvoRayRoutingInputs
        {
            ServerProfile = profile,
            GeoSiteTags = siteTags,
            GeoIpTags = ipTags,
            GameFolders = GamesBypassVpn(settings) ? SvoRayGames.FindGameFolders(settings.CustomGameFolders) : [],
        };

        static IReadOnlySet<string>? ReadTags(string fileName)
        {
            var tags = SvoRayServerRouting.ReadGeoTags(Utils.GetBinPath(fileName));
            return tags.Count > 0 ? tags : null;
        }
    }

    /// <summary>
    /// The profile of the subscription the selected server came from, and only that one. With
    /// several subscriptions another provider's rules must not apply, and a server added on its
    /// own has no provider whose rules it could inherit.
    /// </summary>
    public static async Task<SvoRayServerRoutingProfile?> FindServerProfileAsync(Config config)
    {
        var sub = await FindSelectedSubscriptionAsync(config);
        return SvoRayServerRouting.Parse(sub?.RoutingProfile);
    }

    /// <summary>The subscription of the selected server; null for a server added on its own.</summary>
    public static async Task<SubItem?> FindSelectedSubscriptionAsync(Config config)
    {
        var node = await AppManager.Instance.GetProfileItem(config.IndexId);
        return node?.Subid.IsNotEmpty() == true ? await AppManager.Instance.GetSubItem(node.Subid) : null;
    }

    /// <summary>
    /// Rebuilds the simple profile when it is the active one. Called when another server is
    /// picked: its subscription may send other rules, and the reload that follows would otherwise
    /// keep the previous provider's rules and the game folders found at connect.
    /// </summary>
    public static async Task RefreshIfActiveAsync(Config config)
    {
        if (config.SvoRayItem?.RoutingId.IsNullOrEmpty() != false)
        {
            return;
        }

        var items = await AppManager.Instance.RoutingItems() ?? [];
        if (items.Any(t => t.Id == config.SvoRayItem.RoutingId && t.IsActive))
        {
            await ApplyAsync(config);
        }
    }

    /// <summary>
    /// Whether game mode takes effect. Only in TUN: in proxy mode games ignore the system proxy
    /// and connect directly anyway, so the switch would change nothing. The setting itself is
    /// kept, and comes back with TUN.
    /// </summary>
    public static bool GamesBypassVpn(SvoRayItem settings) =>
        settings.GameMode && settings.Mode == ESvoRayMode.Tun;

    public static List<RulesItem> BuildRules(SvoRayItem settings)
    {
        return BuildRules(settings, new SvoRayRoutingInputs());
    }

    /// <summary>
    /// Builds the simple screen's profile, top to bottom:
    /// <list type="number">
    /// <item>game mode - processes from game folders and launchers go direct, before anything
    /// else can send them into the tunnel or block their QUIC;</item>
    /// <item>the user's own list, which outranks the server's;</item>
    /// <item>the server's block, proxy and direct groups in the order it asked for;</item>
    /// <item>private networks direct, QUIC blocked on the tunnel, everything else by mode.</item>
    /// </list>
    /// </summary>
    public static List<RulesItem> BuildRules(SvoRayItem settings, SvoRayRoutingInputs inputs)
    {
        var rules = new List<RulesItem>();

        if (GamesBypassVpn(settings))
        {
            rules.Add(new RulesItem
            {
                Remarks = ResUI.SvoRayRuleGamesDirect,
                OutboundTag = Global.DirectTag,
                Process = [.. inputs.GameFolders, .. SvoRayGames.ProcessNames]
            });
        }

        rules.AddRange(BuildListRules(settings));

        if (settings.UseServerRouting && inputs.ServerProfile is not null)
        {
            rules.AddRange(BuildServerRules(inputs.ServerProfile, inputs, settings.RoutingMode));
        }

        rules.AddRange(BuildTailRules(settings));
        return rules;
    }

    private static List<RulesItem> BuildServerRules(SvoRayServerRoutingProfile profile, SvoRayRoutingInputs inputs,
        ESvoRayRoutingMode mode)
    {
        var siteFile = profile.GeositeUrl is null ? null : SvoRayServerRouting.GeoSiteFileName;
        var ipFile = profile.GeoipUrl is null ? null : SvoRayServerRouting.GeoIpFileName;
        var dropped = new List<string>();
        List<string> Sites(IEnumerable<string> list) =>
            SvoRayServerRouting.RewriteGeo(list, Global.GeoSitePrefix, siteFile, inputs.GeoSiteTags, dropped);
        List<string> Ips(IEnumerable<string> list) =>
            SvoRayServerRouting.RewriteGeo(list, Global.GeoIPPrefix, ipFile, inputs.GeoIpTags, dropped);

        var rules = new List<RulesItem>();
        foreach (var group in profile.RouteOrder.Split('-'))
        {
            var (tag, remarks, sites, ips) = group switch
            {
                "block" => (Global.BlockTag, ResUI.SvoRayRuleServerBlock, Sites(profile.BlockSites), Ips(profile.BlockIp)),
                "proxy" => (Global.ProxyTag, ResUI.SvoRayRuleServerViaVpn, Sites(profile.ProxySites), Ips(profile.ProxyIp)),
                _ => (Global.DirectTag, ResUI.SvoRayRuleServerDirect, Sites(profile.DirectSites), Ips(profile.DirectIp)),
            };
            if (sites.Count + ips.Count == 0)
            {
                continue;
            }

            // Same reason as for the user's list: QUIC to the tunnel is refused so the browser
            // falls back to TCP. Only in bypass mode, where the final rule blocks it as well.
            if (tag == Global.ProxyTag && mode == ESvoRayRoutingMode.BypassListed)
            {
                rules.Add(new RulesItem
                {
                    Remarks = ResUI.SvoRayRuleQuicViaVpn,
                    OutboundTag = Global.BlockTag,
                    Port = "443",
                    Network = "udp",
                    Domain = sites,
                    Ip = ips
                });
            }

            rules.Add(new RulesItem { Remarks = remarks, OutboundTag = tag, Domain = sites, Ip = ips });
        }

        if (dropped.Count > 0)
        {
            Logging.SaveLog($"SvoRay server routing: left out {string.Join(", ", dropped.Distinct())}");
        }
        return rules;
    }

    private static List<RulesItem> BuildListRules(SvoRayItem settings)
    {
        var domains = (settings.RuleDomains ?? [])
            .Select(NormalizeDomain)
            .Where(domain => domain.IsNotEmpty())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(domain => $"domain:{domain}")
            .ToList();

        var onlyListed = settings.RoutingMode == ESvoRayRoutingMode.ProxyListed;
        var listedTag = onlyListed ? Global.ProxyTag : Global.DirectTag;

        var rules = new List<RulesItem>();

        // QUIC is blocked on whatever goes to the server, the same way the built-in global
        // profile does it: many proxies carry no UDP, and browsers fall back to TCP as soon as
        // the attempt is refused. Traffic that stays direct keeps QUIC.
        if (onlyListed && domains.Count > 0)
        {
            rules.Add(new RulesItem
            {
                Remarks = ResUI.SvoRayRuleQuicViaVpn,
                OutboundTag = Global.BlockTag,
                Port = "443",
                Network = "udp",
                Domain = domains
            });
        }

        if (domains.Count > 0)
        {
            rules.Add(new RulesItem
            {
                Remarks = onlyListed ? ResUI.SvoRayRuleListedViaVpn : ResUI.SvoRayRuleListedWithoutVpn,
                OutboundTag = listedTag,
                Domain = domains
            });
        }

        return rules;
    }

    private static List<RulesItem> BuildTailRules(SvoRayItem settings)
    {
        var onlyListed = settings.RoutingMode == ESvoRayRoutingMode.ProxyListed;
        var finalTag = onlyListed ? Global.DirectTag : Global.ProxyTag;
        var rules = new List<RulesItem>();

        rules.Add(new RulesItem
        {
            Remarks = ResUI.SvoRayRulePrivateNetwork,
            OutboundTag = Global.DirectTag,
            Ip = ["geoip:private"]
        });
        rules.Add(new RulesItem
        {
            Remarks = ResUI.SvoRayRulePrivateDomains,
            OutboundTag = Global.DirectTag,
            Domain = ["geosite:private"]
        });

        if (!onlyListed)
        {
            rules.Add(new RulesItem
            {
                Remarks = ResUI.SvoRayRuleQuicViaVpn,
                OutboundTag = Global.BlockTag,
                Port = "443",
                Network = "udp"
            });
        }

        rules.Add(new RulesItem
        {
            Remarks = onlyListed ? ResUI.SvoRayRuleRestDirect : ResUI.SvoRayRuleRestViaVpn,
            OutboundTag = finalTag,
            Port = "0-65535"
        });

        return rules;
    }

    /// <summary>
    /// Reduces what the user typed to a bare host name. A pasted URL, a port, a wildcard or a
    /// "www." prefix all name the same site to a person, and every one of them would otherwise be
    /// stored as a matcher that never fires. Returns an empty string when nothing usable is left.
    /// </summary>
    public static string NormalizeDomain(string? value)
    {
        var text = value.TrimEx().ToLowerInvariant();
        if (text.IsNullOrEmpty())
        {
            return string.Empty;
        }

        if (text.Contains("://"))
        {
            text = Uri.TryCreate(text, UriKind.Absolute, out var uri) && uri.IdnHost.IsNotEmpty()
                ? uri.IdnHost
                : text[(text.IndexOf("://", StringComparison.Ordinal) + 3)..];
        }

        text = text.Split('/')[0].Split('?')[0].Split('@')[^1];

        // An IPv6 literal would lose its meaning as a domain matcher; host names only.
        if (text.Contains('['))
        {
            return string.Empty;
        }

        var portSeparator = text.LastIndexOf(':');
        if (portSeparator > 0)
        {
            text = text[..portSeparator];
        }

        text = text.Trim('.');
        if (text.StartsWith("*."))
        {
            text = text[2..];
        }
        if (text.StartsWith("www."))
        {
            text = text[4..];
        }

        if (!text.Contains('.') || text.Length > 253)
        {
            return string.Empty;
        }

        // A bare address is not a domain matcher. Pasting a hosts file, where every line starts
        // with 0.0.0.0 or 127.0.0.1, would otherwise fill the list with entries routing nothing.
        if (IPAddress.TryParse(text, out _))
        {
            return string.Empty;
        }

        return text.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_')
            ? text
            : string.Empty;
    }
}

/// <summary>
/// Everything <see cref="SvoRayRoutingHandler.BuildRules(SvoRayItem, SvoRayRoutingInputs)"/>
/// needs beyond the user's settings, gathered once per connect so the rule building itself
/// stays a pure function the tests can drive.
/// </summary>
public sealed class SvoRayRoutingInputs
{
    /// <summary>Profile from the subscription, or null when it sends none or it is switched off.</summary>
    public SvoRayServerRoutingProfile? ServerProfile { get; init; }

    /// <summary>Categories in the geo file the profile's site lists use; null when unavailable.</summary>
    public IReadOnlySet<string>? GeoSiteTags { get; init; }

    /// <summary>Categories in the geo file the profile's address lists use; null when unavailable.</summary>
    public IReadOnlySet<string>? GeoIpTags { get; init; }

    /// <summary>Game library and launcher folders, as Xray folder entries ending in a slash.</summary>
    public IReadOnlyList<string> GameFolders { get; init; } = [];
}
