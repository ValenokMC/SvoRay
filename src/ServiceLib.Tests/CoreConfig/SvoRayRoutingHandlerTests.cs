using AwesomeAssertions;
using ServiceLib.Common;
using ServiceLib.Enums;
using ServiceLib.Handler;
using ServiceLib.Models;
using ServiceLib.Services.CoreConfig;
using Xunit;

namespace ServiceLib.Tests.CoreConfig;

public class SvoRayRoutingHandlerTests
{
    [Theory]
    [InlineData("example.com", "example.com")]
    [InlineData("  Example.COM  ", "example.com")]
    [InlineData("https://www.example.com/path?q=1", "example.com")]
    [InlineData("example.com:443", "example.com")]
    [InlineData("*.example.com", "example.com")]
    [InlineData("example.com.", "example.com")]
    [InlineData("sub.example.co.uk", "sub.example.co.uk")]
    public void NormalizeDomain_ShouldReduceToBareHost(string input, string expected)
    {
        SvoRayRoutingHandler.NormalizeDomain(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("localhost")]
    [InlineData("два слова.рф")]
    [InlineData("http://[2001:db8::1]:8080/")]
    public void NormalizeDomain_ShouldRejectWhatCannotBecomeAMatcher(string? input)
    {
        SvoRayRoutingHandler.NormalizeDomain(input).Should().BeEmpty();
    }

    [Fact]
    public void ParseDomains_ShouldAcceptAPastedListInAnyShape()
    {
        var parsed = SvoRayRoutingHandler.ParseDomains(
            "gosuslugi.ru\r\nhttps://www.sberbank.ru/ru/person\n avito.ru, vk.com; ozon.ru\tsberbank.ru");

        parsed.Should().Equal("gosuslugi.ru", "sberbank.ru", "avito.ru", "vk.com", "ozon.ru");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" , ; \n ")]
    [InlineData("localhost\nне домен")]
    public void ParseDomains_ShouldReturnNothingWhenThereIsNoUsableDomain(string? input)
    {
        SvoRayRoutingHandler.ParseDomains(input).Should().BeEmpty();
    }

    /// <summary>
    /// Pasting a hosts file is a plausible thing to try, and every line of one begins with an
    /// address that would otherwise be stored as a domain matching nothing.
    /// </summary>
    [Fact]
    public void ParseDomains_ShouldKeepAddressesOutOfADomainList()
    {
        var parsed = SvoRayRoutingHandler.ParseDomains("0.0.0.0 tracker.example\n127.0.0.1 ads.example");

        parsed.Should().Equal("tracker.example", "ads.example");
    }

    /// <summary>
    /// The maintained list is fetched at runtime, so this pins the parsing of its actual shape:
    /// one host per line, some of them deep subdomains, some punycode.
    /// </summary>
    [Fact]
    public void ParseDomains_ShouldReadTheMaintainedListFormat()
    {
        var payload = string.Join('\n',
            "gosuslugi.ru",
            "gu-st.ru",
            "b2c-ticket-sentry.onelya.ru",
            "xn--90aijkdmaud0d.xn--p1ai",
            "1018213540.rsc.cdn77.org");

        SvoRayRoutingHandler.ParseDomains(payload).Should().Equal(
            "gosuslugi.ru",
            "gu-st.ru",
            "b2c-ticket-sentry.onelya.ru",
            "xn--90aijkdmaud0d.xn--p1ai",
            "1018213540.rsc.cdn77.org");
    }

    /// <summary>
    /// Every entry has to survive the same normalisation a typed domain goes through, or the
    /// preset would quietly ship matchers that never fire.
    /// </summary>
    [Fact]
    public void RussiaPreset_ShouldBeNormalisedAndFreeOfDuplicates()
    {
        var preset = SvoRayRoutingHandler.RussiaPreset;

        preset.Should().OnlyContain(domain => SvoRayRoutingHandler.NormalizeDomain(domain) == domain);
        preset.Should().OnlyHaveUniqueItems();
        preset.Should().Contain(["gosuslugi.ru", "sberbank.ru", "yandex.ru", "avito.ru"]);
    }

    /// <summary>
    /// An entry already covered by a shorter one in the same list is dead weight: the matcher
    /// takes subdomains, so gov.ru answers for nalog.gov.ru and the pair would only mislead a
    /// reader into thinking one of them does something the other does not.
    /// </summary>
    [Fact]
    public void RussiaPreset_ShouldNotRepeatWhatAShorterEntryAlreadyCovers()
    {
        var preset = SvoRayRoutingHandler.RussiaPreset;

        var covered = preset
            .Where(domain => preset.Any(other => other != domain && domain.EndsWith($".{other}", StringComparison.Ordinal)))
            .ToList();

        covered.Should().BeEmpty();
    }

    [Fact]
    public void RussiaPreset_ShouldCarryTheStaticHostsOfTheSitesItLists()
    {
        var preset = SvoRayRoutingHandler.RussiaPreset;

        // A site whose pages are direct while its images still go through the tunnel is the
        // failure this pairing exists to prevent.
        var pairs = new[]
        {
            ("gosuslugi.ru", "gu-st.ru"),
            ("yandex.ru", "yastatic.net"),
            ("vk.com", "userapi.com"),
            ("ozon.ru", "ozone.ru"),
            ("wildberries.ru", "wbbasket.ru"),
            ("avito.ru", "avito.st"),
            ("mail.ru", "imgsmail.ru"),
        };

        foreach (var (site, statics) in pairs)
        {
            preset.Should().Contain(site);
            preset.Should().Contain(statics);
        }
    }

    [Fact]
    public void RussiaPreset_ShouldSurviveRuleGenerationAsBypassDomains()
    {
        var rules = SvoRayRoutingHandler.BuildRules(new SvoRayItem
        {
            RoutingMode = ESvoRayRoutingMode.BypassListed,
            RuleDomains = [.. SvoRayRoutingHandler.RussiaPreset]
        });

        var listed = rules.First();
        listed.OutboundTag.Should().Be(Global.DirectTag);
        listed.Domain.Should().HaveCount(SvoRayRoutingHandler.RussiaPreset.Count);
        listed.Domain.Should().Contain("domain:gosuslugi.ru");
    }

    [Fact]
    public void BuildRules_BypassListed_ShouldSendListedDomainsDirectAndTheRestToProxy()
    {
        var rules = SvoRayRoutingHandler.BuildRules(new SvoRayItem
        {
            RoutingMode = ESvoRayRoutingMode.BypassListed,
            RuleDomains = ["https://www.bank.example/login", "bank.example", "shop.example"]
        });

        var listed = rules.First();
        listed.OutboundTag.Should().Be(Global.DirectTag);

        // The two spellings of the same host must collapse into one matcher.
        listed.Domain.Should().Equal("domain:bank.example", "domain:shop.example");

        var final = rules.Last();
        final.OutboundTag.Should().Be(Global.ProxyTag);
        final.Port.Should().Be("0-65535");

        rules.Should().Contain(r => r.OutboundTag == Global.BlockTag && r.Port == "443" && r.Network == "udp");
    }

    [Fact]
    public void BuildRules_ProxyListed_ShouldSendOnlyListedDomainsToProxy()
    {
        var rules = SvoRayRoutingHandler.BuildRules(new SvoRayItem
        {
            RoutingMode = ESvoRayRoutingMode.ProxyListed,
            RuleDomains = ["blocked.example"]
        });

        rules.Should().Contain(r => r.OutboundTag == Global.ProxyTag
            && r.Domain != null && r.Domain.Contains("domain:blocked.example"));

        var final = rules.Last();
        final.OutboundTag.Should().Be(Global.DirectTag);
        final.Port.Should().Be("0-65535");

        // QUIC is only refused for what actually goes to the server.
        rules.Should().NotContain(r => r.OutboundTag == Global.BlockTag && r.Domain == null);
        rules.Should().Contain(r => r.OutboundTag == Global.BlockTag
            && r.Network == "udp"
            && r.Domain != null && r.Domain.Contains("domain:blocked.example"));
    }

    [Fact]
    public void BuildRules_WithoutDomains_ShouldStillCoverLanAndFinalHop()
    {
        var rules = SvoRayRoutingHandler.BuildRules(new SvoRayItem { RuleDomains = [] });

        rules.Should().Contain(r => r.OutboundTag == Global.DirectTag
            && r.Ip != null && r.Ip.Contains("geoip:private"));
        rules.Should().Contain(r => r.OutboundTag == Global.DirectTag
            && r.Domain != null && r.Domain.Contains("geosite:private"));
        rules.Last().OutboundTag.Should().Be(Global.ProxyTag);
    }

    /// <summary>
    /// The point of expressing the bypass list as ordinary routing rules: the core config
    /// services turn a domain routed to direct into a DNS rule as well, so the name is resolved
    /// outside the tunnel too. Resolving it through the proxy would hand back an address chosen
    /// for the wrong country, which is exactly what the user asked to avoid for this domain.
    /// </summary>
    [Fact]
    public void GeneratedRules_ShouldReachSingboxAsBothARouteAndADnsRule()
    {
        var config = CoreConfigTestFactory.CreateConfig(ECoreType.sing_box);
        CoreConfigTestFactory.BindAppManagerConfig(config);
        var node = CoreConfigTestFactory.CreateSocksNode(ECoreType.sing_box);
        var context = CoreConfigTestFactory.CreateContext(config, node, ECoreType.sing_box);
        context.RoutingItem.RuleSet = JsonUtils.Serialize(SvoRayRoutingHandler.BuildRules(new SvoRayItem
        {
            RoutingMode = ESvoRayRoutingMode.BypassListed,
            RuleDomains = ["bank.example"]
        }), false);

        var result = new CoreConfigSingboxService(context).GenerateClientConfigContent();

        result.Success.Should().BeTrue($"ret msg: {result.Msg}");
        var singboxConfig = JsonUtils.Deserialize<SingboxConfig>(result.Data!.ToString())!;

        singboxConfig.route.rules.Should().Contain(r => r.outbound == Global.DirectTag
            && r.domain_suffix != null && r.domain_suffix.Contains("bank.example"));
        singboxConfig.dns.rules.Should().Contain(r => r.server == Global.SingboxDirectDNSTag
            && r.domain_suffix != null && r.domain_suffix.Contains("bank.example"));
        singboxConfig.dns.final.Should().Be(Global.SingboxRemoteDNSTag);
    }

    /// <summary>
    /// In split-tunnel mode everything unlisted stays on the local network, so the resolver that
    /// answers by default has to be the direct one - otherwise every ordinary lookup would still
    /// travel through the proxy.
    /// </summary>
    [Fact]
    public void GeneratedRules_ProxyListed_ShouldResolveEverythingElseDirectly()
    {
        var config = CoreConfigTestFactory.CreateConfig(ECoreType.sing_box);
        CoreConfigTestFactory.BindAppManagerConfig(config);
        var node = CoreConfigTestFactory.CreateSocksNode(ECoreType.sing_box);
        var context = CoreConfigTestFactory.CreateContext(config, node, ECoreType.sing_box);
        context.RoutingItem.RuleSet = JsonUtils.Serialize(SvoRayRoutingHandler.BuildRules(new SvoRayItem
        {
            RoutingMode = ESvoRayRoutingMode.ProxyListed,
            RuleDomains = ["blocked.example"]
        }), false);

        var result = new CoreConfigSingboxService(context).GenerateClientConfigContent();

        result.Success.Should().BeTrue($"ret msg: {result.Msg}");
        var singboxConfig = JsonUtils.Deserialize<SingboxConfig>(result.Data!.ToString())!;

        singboxConfig.route.rules.Should().Contain(r => r.outbound == Global.ProxyTag
            && r.domain_suffix != null && r.domain_suffix.Contains("blocked.example"));
        singboxConfig.dns.rules.Should().Contain(r => r.server == Global.SingboxRemoteDNSTag
            && r.domain_suffix != null && r.domain_suffix.Contains("blocked.example"));
        singboxConfig.dns.final.Should().Be(Global.SingboxDirectDNSTag);
    }
}
