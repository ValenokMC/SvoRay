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
