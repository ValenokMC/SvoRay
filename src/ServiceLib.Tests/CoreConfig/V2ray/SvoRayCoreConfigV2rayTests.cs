using ServiceLib.Common;
using ServiceLib.Enums;
using ServiceLib.Models;
using ServiceLib.Services.CoreConfig;

namespace ServiceLib.Tests.CoreConfig.V2ray;

// Inside the namespace: this scope is searched first, so Should() binds to AwesomeAssertions, not to TUnit's global Should().
using AwesomeAssertions;

/// <summary>SvoRay's additions to the Xray config: the TUN loop guard and the kill switch's own DNS.</summary>
[NotInParallel(nameof(SvoRayCoreConfigV2rayTests))]
public class SvoRayCoreConfigV2rayTests
{
    [Test]
    public void GenerateClientConfigContent_Tun_ShouldDropTheCoresOwnTrafficComingBackThroughTheTun()
    {
        var config = CoreConfigTestFactory.CreateConfigWithTunRouteExcludeAddress(ECoreType.Xray);
        CoreConfigTestFactory.BindAppManagerConfig(config);
        var node = CoreConfigTestFactory.CreateVmessNode(ECoreType.Xray, "n-main", "main");
        var context = CoreConfigTestFactory.CreateContext(config, node, ECoreType.Xray);

        var result = new CoreConfigV2rayService(context).GenerateClientConfigContent();

        result.Success.Should().BeTrue();
        var rules = JsonUtils.Deserialize<V2rayConfig>(result.Data!.ToString())!.routing.rules;
        var loopGuard = rules.FindIndex(r => r.outboundTag == Global.BlockTag
            && r.inboundTag?.SequenceEqual(["tun"]) == true
            && r.process?.SequenceEqual(["self/"]) == true);
        loopGuard.Should().BeGreaterThanOrEqualTo(0);
        // Before every rule that would send the core's own traffic anywhere else.
        rules.FindIndex(r => r.process?.Contains("self/") == true && r.outboundTag != Global.BlockTag)
            .Should().BeGreaterThan(loopGuard);
    }

    [Test]
    [Arguments(true, null, Global.KillSwitchDomainStrategy)]
    [Arguments(true, Global.AsIs, Global.KillSwitchDomainStrategy)]
    [Arguments(true, "UseIPv4", "UseIPv4")]
    [Arguments(false, null, null)]
    public void GenerateClientConfigContent_KillSwitch_ShouldResolveWithTheCoresOwnDns(
        bool killSwitch, string? userStrategy, string? expected)
    {
        // Windows' DNS Client service is cut off by the kill switch; the core must not depend on it
        // to find its own server or a direct site.
        var config = CoreConfigTestFactory.CreateConfig(ECoreType.Xray);
        config.SvoRayItem = new SvoRayItem { KillSwitch = killSwitch };
        config.SimpleDNSItem.Strategy4Freedom = userStrategy;
        config.SimpleDNSItem.Strategy4ProxyDial = userStrategy;
        CoreConfigTestFactory.BindAppManagerConfig(config);
        var node = CoreConfigTestFactory.CreateVmessNode(ECoreType.Xray);
        var context = CoreConfigTestFactory.CreateContext(config, node, ECoreType.Xray) with
        {
            ProtectDomainList = [node.Address],
        };

        var result = new CoreConfigV2rayService(context).GenerateClientConfigContent();

        result.Success.Should().BeTrue();
        var cfg = JsonUtils.Deserialize<V2rayConfig>(result.Data!.ToString())!;
        var proxy = cfg.outbounds.First(o => o.tag == Global.ProxyTag);
        var direct = cfg.outbounds.First(o => o.tag == Global.DirectTag && o.protocol == "freedom");
        proxy.streamSettings?.sockopt?.domainStrategy.Should().Be(expected);
        direct.streamSettings?.sockopt?.domainStrategy.Should().Be(expected);

        if (killSwitch)
        {
            // The server's own name is answered by a direct DNS server, which the core asks itself.
            var dns = JsonUtils.Deserialize<Dns4Ray>(JsonUtils.Serialize(cfg.dns))!;
            dns.servers
                .Select(s => JsonUtils.Deserialize<DnsServer4Ray>(JsonUtils.Serialize(s)))
                .Should().Contain(s => s != null
                    && s.tag != null && s.tag.StartsWith(Global.DirectDnsTag)
                    && s.domains != null && s.domains.Contains(node.Address));
        }
    }

    [Test]
    public void GenerateClientConfigContent_DnsModuleRules_ShouldComeBeforeTheCatchAll()
    {
        // The usual routing ends with "every port -> proxy". The DNS module's own rules must be
        // looked at before it, or a direct DNS query goes to the proxy - and when that query is
        // for the proxy server itself, the tunnel can never come up.
        var config = CoreConfigTestFactory.CreateConfig(ECoreType.Xray);
        config.SvoRayItem = new SvoRayItem { KillSwitch = true };
        CoreConfigTestFactory.BindAppManagerConfig(config);
        var node = CoreConfigTestFactory.CreateVmessNode(ECoreType.Xray);
        var context = CoreConfigTestFactory.CreateContext(config, node, ECoreType.Xray) with
        {
            ProtectDomainList = [node.Address],
            RoutingItem = new RoutingItem
            {
                Id = "r-final",
                Remarks = "final proxy",
                RuleSet = JsonUtils.Serialize(new List<RulesItem>
                {
                    new()
                    {
                        Enabled = true,
                        RuleType = ERuleType.Routing,
                        OutboundTag = Global.ProxyTag,
                        Port = "0-65535",
                    },
                }),
                DomainStrategy = Global.AsIs,
                DomainStrategy4Singbox = string.Empty,
            },
        };

        var result = new CoreConfigV2rayService(context).GenerateClientConfigContent();

        result.Success.Should().BeTrue();
        var rules = JsonUtils.Deserialize<V2rayConfig>(result.Data!.ToString())!.routing.rules;
        var catchAll = rules.FindIndex(r => r.port == "0-65535" && r.inboundTag == null);
        var directDns = rules.FindIndex(r => r.inboundTag?.Any(t => t.StartsWith(Global.DirectDnsTag)) == true);
        var dnsModule = rules.FindIndex(r => r.inboundTag?.Contains(Global.DnsTag) == true);

        catchAll.Should().BeGreaterThanOrEqualTo(0);
        directDns.Should().BeInRange(0, catchAll - 1);
        rules[directDns].outboundTag.Should().Be(Global.DirectTag);
        dnsModule.Should().BeInRange(0, catchAll - 1);
    }
}
