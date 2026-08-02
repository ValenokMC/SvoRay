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
        // State services
        "gosuslugi.ru",
        "gu-st.ru",
        "nalog.ru",
        "nalog.gov.ru",
        "sfr.gov.ru",
        "gibdd.ru",
        "mos.ru",
        "pochta.ru",
        "rzd.ru",

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
        "wildberries.ru",
        "wbbasket.ru",
        "wbstatic.net",
        "avito.ru",
        "avito.st",
        "dns-shop.ru",
        "citilink.ru",
        "mvideo.ru",
        "2gis.ru",
        "hh.ru",
        "cian.ru",
        "domclick.ru",
        "drom.ru",

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

        var rules = BuildRules(config.SvoRayItem);
        if (await ConfigHandler.AddBatchRoutingRules(item, JsonUtils.Serialize(rules, false)) != 0)
        {
            return;
        }

        config.SvoRayItem.RoutingId = item.Id;
        await ConfigHandler.SetDefaultRouting(config, item);
    }

    public static List<RulesItem> BuildRules(SvoRayItem settings)
    {
        var domains = (settings.RuleDomains ?? [])
            .Select(NormalizeDomain)
            .Where(domain => domain.IsNotEmpty())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(domain => $"domain:{domain}")
            .ToList();

        var onlyListed = settings.RoutingMode == ESvoRayRoutingMode.ProxyListed;
        var listedTag = onlyListed ? Global.ProxyTag : Global.DirectTag;
        var finalTag = onlyListed ? Global.DirectTag : Global.ProxyTag;

        var rules = new List<RulesItem>();

        // QUIC is blocked on whatever goes to the server, the same way the built-in global
        // profile does it: many proxies carry no UDP, and browsers fall back to TCP as soon as
        // the attempt is refused. Traffic that stays direct keeps QUIC.
        if (onlyListed && domains.Count > 0)
        {
            rules.Add(new RulesItem
            {
                Remarks = "SvoRay: QUIC через VPN",
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
                Remarks = onlyListed ? "SvoRay: только эти домены через VPN" : "SvoRay: эти домены без VPN",
                OutboundTag = listedTag,
                Domain = domains
            });
        }

        rules.Add(new RulesItem
        {
            Remarks = "SvoRay: локальная сеть",
            OutboundTag = Global.DirectTag,
            Ip = ["geoip:private"]
        });
        rules.Add(new RulesItem
        {
            Remarks = "SvoRay: локальные домены",
            OutboundTag = Global.DirectTag,
            Domain = ["geosite:private"]
        });

        if (!onlyListed)
        {
            rules.Add(new RulesItem
            {
                Remarks = "SvoRay: QUIC через VPN",
                OutboundTag = Global.BlockTag,
                Port = "443",
                Network = "udp"
            });
        }

        rules.Add(new RulesItem
        {
            Remarks = onlyListed ? "SvoRay: остальное напрямую" : "SvoRay: остальное через VPN",
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

        return text.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_')
            ? text
            : string.Empty;
    }
}
