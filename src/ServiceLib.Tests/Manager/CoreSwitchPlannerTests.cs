using AwesomeAssertions;
using ServiceLib.Enums;
using ServiceLib.Manager;
using Xunit;

namespace ServiceLib.Tests.Manager;

public class CoreSwitchPlannerTests
{
    private static readonly CoreConfigShape ShapeA = new("FP1", "proxy-1");
    private static readonly CoreConfigShape ShapeB = new("FP1", "proxy-2");
    private static readonly RunningCoreState RunningA = new(ECoreType.sing_box, ShapeA);

    [Fact]
    public void Decide_SameConfigOtherServer_HotSwitches()
    {
        CoreSwitchPlanner.Decide(RunningA, coreAlive: true, ECoreType.sing_box, hasPreCore: false, ShapeB)
            .Should().Be(ECoreSwitchKind.HotSwitch);
    }

    [Fact]
    public void Decide_NothingRunning_Restarts()
    {
        CoreSwitchPlanner.Decide(null, coreAlive: false, ECoreType.sing_box, hasPreCore: false, ShapeB)
            .Should().Be(ECoreSwitchKind.Restart);
    }

    [Fact]
    public void Decide_CoreDied_Restarts()
    {
        CoreSwitchPlanner.Decide(RunningA, coreAlive: false, ECoreType.sing_box, hasPreCore: false, ShapeB)
            .Should().Be(ECoreSwitchKind.Restart);
    }

    [Fact]
    public void Decide_ConfigChangedBesidesTheServer_Restarts()
    {
        // A mode, routing, DNS or server-set change shows up as a different fingerprint.
        var changed = new CoreConfigShape("FP2", "proxy-2");
        CoreSwitchPlanner.Decide(RunningA, coreAlive: true, ECoreType.sing_box, hasPreCore: false, changed)
            .Should().Be(ECoreSwitchKind.Restart);
    }

    [Fact]
    public void Decide_OtherCoreType_Restarts()
    {
        CoreSwitchPlanner.Decide(RunningA, coreAlive: true, ECoreType.Xray, hasPreCore: false, ShapeB)
            .Should().Be(ECoreSwitchKind.Restart);
        CoreSwitchPlanner.Decide(RunningA with { CoreType = ECoreType.Xray }, coreAlive: true, ECoreType.sing_box,
                hasPreCore: false, ShapeB)
            .Should().Be(ECoreSwitchKind.Restart);
    }

    [Fact]
    public void Decide_NewConfigWithoutSelector_Restarts()
    {
        CoreSwitchPlanner.Decide(RunningA, coreAlive: true, ECoreType.sing_box, hasPreCore: false, null)
            .Should().Be(ECoreSwitchKind.Restart);
    }

    [Fact]
    public void Decide_PreCore_Restarts()
    {
        CoreSwitchPlanner.Decide(RunningA, coreAlive: true, ECoreType.sing_box, hasPreCore: true, ShapeB)
            .Should().Be(ECoreSwitchKind.Restart);
    }

    [Fact]
    public void Decide_SameServerAgain_Restarts()
    {
        // A reload of the running server is asked for on purpose and keeps restarting the core.
        CoreSwitchPlanner.Decide(RunningA, coreAlive: true, ECoreType.sing_box, hasPreCore: false, ShapeA)
            .Should().Be(ECoreSwitchKind.Restart);
    }

    [Fact]
    public void Decide_Xray_HotSwitchesUntilTheSwapLimit()
    {
        var running = new RunningCoreState(ECoreType.Xray, ShapeA);

        CoreSwitchPlanner.Decide(running, coreAlive: true, ECoreType.Xray, hasPreCore: false, ShapeB)
            .Should().Be(ECoreSwitchKind.HotSwitch);
        CoreSwitchPlanner.Decide(running with { HotSwitches = CoreSwitchPlanner.MaxXrayHotSwitches },
                coreAlive: true, ECoreType.Xray, hasPreCore: false, ShapeB)
            .Should().Be(ECoreSwitchKind.Restart, "removed Xray outbounds are never closed, a restart clears them");
    }

    [Fact]
    public void Decide_OtherCores_AlwaysRestart()
    {
        var running = new RunningCoreState(ECoreType.mihomo, ShapeA);
        CoreSwitchPlanner.Decide(running, coreAlive: true, ECoreType.mihomo, hasPreCore: false, ShapeB)
            .Should().Be(ECoreSwitchKind.Restart);
    }

    [Fact]
    public void ReadXrayShape_MasksOnlyTheProxyOutbound()
    {
        var a = CoreSwitchPlanner.ReadXrayShape(XrayConfig("a.example.com", "1.1.1.1"));
        var b = CoreSwitchPlanner.ReadXrayShape(XrayConfig("b.example.com", "1.1.1.1"));
        var otherDns = CoreSwitchPlanner.ReadXrayShape(XrayConfig("b.example.com", "8.8.8.8"));

        a.Should().NotBeNull();
        b!.Fingerprint.Should().Be(a!.Fingerprint);
        b.Selection.Should().NotBe(a.Selection);
        b.Selection.Should().Contain("b.example.com").And.Contain("\"tag\":\"proxy\"");
        otherDns!.Fingerprint.Should().NotBe(a.Fingerprint);
    }

    [Fact]
    public void ReadXrayShape_WithoutHandlerApiOrWithBalancer_ReturnsNull()
    {
        var noApi = JsonNode.Parse(XrayConfig("a.example.com", "1.1.1.1"))!.AsObject();
        noApi.Remove("api");
        var balancer = JsonNode.Parse(XrayConfig("a.example.com", "1.1.1.1"))!.AsObject();
        balancer["routing"] = JsonNode.Parse("""{"balancers":[{"tag":"proxy-balancer","selector":["proxy"]}]}""");

        CoreSwitchPlanner.ReadXrayShape(noApi.ToJsonString()).Should().BeNull();
        CoreSwitchPlanner.ReadXrayShape(balancer.ToJsonString()).Should().BeNull();
        CoreSwitchPlanner.ReadXrayShape(null).Should().BeNull();
    }

    [Fact]
    public void WrapOutbound_GivesTheConfigShapeTheXrayCommandsRead()
    {
        var wrapped = JsonNode.Parse(XrayApiService.WrapOutbound("""{"tag":"proxy","protocol":"vless"}"""))!;

        wrapped["outbounds"]!.AsArray().Should().ContainSingle();
        ((string?)wrapped["outbounds"]![0]!["tag"]).Should().Be("proxy");
    }

    [Theory]
    [InlineData(true, true, true, true)]
    [InlineData(true, true, false, false)]
    [InlineData(true, false, true, false)]
    [InlineData(false, true, true, false)]
    public void ShouldRemoveTunDevices_OnlyForTunOnWindowsWhenAdapterMayBeStale(bool isWindows, bool isTun,
        bool mayBeStale, bool expected)
    {
        CoreSwitchPlanner.ShouldRemoveTunDevices(isWindows, isTun, mayBeStale).Should().Be(expected);
    }

    [Fact]
    public void CanKeepRunningCore_OnlyForTheConfigItRuns()
    {
        var running = CoreSwitchPlanner.ReadConfigDigest(Config("proxy-1", "8.8.8.8"));
        var same = CoreSwitchPlanner.ReadConfigDigest(Config("proxy-1", "8.8.8.8"));
        var otherServer = CoreSwitchPlanner.ReadConfigDigest(Config("proxy-2", "8.8.8.8"));

        CoreSwitchPlanner.CanKeepRunningCore(running, coreAlive: true, same).Should().BeTrue();
        CoreSwitchPlanner.CanKeepRunningCore(running, coreAlive: true, otherServer).Should().BeFalse();
        CoreSwitchPlanner.CanKeepRunningCore(running, coreAlive: false, same).Should().BeFalse("a dead core is restarted");
        CoreSwitchPlanner.CanKeepRunningCore(null, coreAlive: false, same).Should().BeFalse("nothing runs yet");
        CoreSwitchPlanner.CanKeepRunningCore(null, coreAlive: true, null).Should().BeFalse("a custom config is not compared");
    }

    [Fact]
    public void ReadConfigDigest_IgnoresDatedLogFilesOnly()
    {
        const string today = """{"log":{"loglevel":"warning","access":"C:\\logs\\Vaccess_2026-11-26.txt","error":"C:\\logs\\Verror_2026-11-26.txt"},"outbounds":[{"tag":"proxy"}]}""";
        const string tomorrow = """{"log":{"loglevel":"warning","access":"C:\\logs\\Vaccess_2026-11-27.txt","error":"C:\\logs\\Verror_2026-11-27.txt"},"outbounds":[{"tag":"proxy"}]}""";
        const string singbox = """{"log":{"level":"warning","output":"C:\\logs\\sbox_2026-11-26.txt"},"outbounds":[{"tag":"proxy"}]}""";
        const string singboxTomorrow = """{"log":{"level":"warning","output":"C:\\logs\\sbox_2026-11-27.txt"},"outbounds":[{"tag":"proxy"}]}""";
        const string otherLevel = """{"log":{"loglevel":"debug","access":"C:\\logs\\Vaccess_2026-11-26.txt"},"outbounds":[{"tag":"proxy"}]}""";

        CoreSwitchPlanner.ReadConfigDigest(tomorrow).Should().Be(CoreSwitchPlanner.ReadConfigDigest(today));
        CoreSwitchPlanner.ReadConfigDigest(singboxTomorrow).Should().Be(CoreSwitchPlanner.ReadConfigDigest(singbox));
        CoreSwitchPlanner.ReadConfigDigest(otherLevel).Should().NotBe(CoreSwitchPlanner.ReadConfigDigest(today));
    }

    [Fact]
    public void ReadConfigDigest_CoversThePreCore()
    {
        var main = Config("proxy-1", "8.8.8.8");

        var withPre = CoreSwitchPlanner.ReadConfigDigest(main, """{"inbounds":[{"type":"tun"}]}""");

        withPre.Should().NotBe(CoreSwitchPlanner.ReadConfigDigest(main));
        CoreSwitchPlanner.ReadConfigDigest(main, """{"inbounds":[{"type":"mixed"}]}""").Should().NotBe(withPre);
        CoreSwitchPlanner.ReadConfigDigest(main, "").Should().BeNull("a pre config that failed is never the running one");
        CoreSwitchPlanner.ReadConfigDigest("not json").Should().BeNull();
    }

    [Fact]
    public void ReadSingboxShape_IgnoresOnlyTheSelectorDefault()
    {
        var a = CoreSwitchPlanner.ReadSingboxShape(Config("proxy-1", "8.8.8.8"));
        var b = CoreSwitchPlanner.ReadSingboxShape(Config("proxy-2", "8.8.8.8"));
        var other = CoreSwitchPlanner.ReadSingboxShape(Config("proxy-2", "1.1.1.1"));

        a.Should().NotBeNull();
        a!.Selection.Should().Be("proxy-1");
        b!.Selection.Should().Be("proxy-2");
        b.Fingerprint.Should().Be(a.Fingerprint);
        other!.Fingerprint.Should().NotBe(a.Fingerprint);
    }

    [Fact]
    public void ReadSingboxShape_NoManualSelector_ReturnsNull()
    {
        const string singleServer = """{"outbounds":[{"type":"vmess","tag":"proxy"},{"type":"direct","tag":"direct"}]}""";
        // A policy group's selector has no default and starts on its urltest member.
        const string autoGroup = """{"outbounds":[{"type":"selector","tag":"proxy","outbounds":["proxy-auto","proxy-1"]},{"type":"urltest","tag":"proxy-auto","outbounds":["proxy-1"]}]}""";
        const string defaultNotMember = """{"outbounds":[{"type":"selector","tag":"proxy","outbounds":["proxy-1"],"default":"proxy-9"}]}""";

        CoreSwitchPlanner.ReadSingboxShape(singleServer).Should().BeNull();
        CoreSwitchPlanner.ReadSingboxShape(autoGroup).Should().BeNull();
        CoreSwitchPlanner.ReadSingboxShape(defaultNotMember).Should().BeNull();
        CoreSwitchPlanner.ReadSingboxShape("not json").Should().BeNull();
        CoreSwitchPlanner.ReadSingboxShape(null).Should().BeNull();
    }

    private static string XrayConfig(string server, string dns) => $$"""
        {
          "api": { "tag": "api", "listen": "127.0.0.1:10815", "services": [ "HandlerService" ] },
          "dns": { "servers": [ "{{dns}}" ] },
          "outbounds": [
            { "tag": "proxy", "protocol": "vless", "settings": { "address": "{{server}}", "port": 443 } },
            { "tag": "direct", "protocol": "freedom" }
          ],
          "routing": { "rules": [ { "outboundTag": "proxy", "network": "tcp,udp" } ] }
        }
        """;

    private static string Config(string selected, string dns) => $$"""
        {
          "dns": { "servers": [ { "server": "{{dns}}" } ] },
          "outbounds": [
            { "type": "selector", "tag": "proxy", "outbounds": [ "proxy-1", "proxy-2" ], "default": "{{selected}}", "interrupt_exist_connections": true },
            { "type": "vmess", "tag": "proxy-1", "server": "a.example.com" },
            { "type": "vmess", "tag": "proxy-2", "server": "b.example.com" }
          ]
        }
        """;
}
