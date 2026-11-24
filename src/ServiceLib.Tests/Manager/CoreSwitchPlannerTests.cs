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
    public void ReadSingboxShape_IgnoresOnlyTheSelectorDefault()
    {
        var a = CoreSwitchPlanner.ReadSingboxShape(Config("proxy-1", "8.8.8.8"));
        var b = CoreSwitchPlanner.ReadSingboxShape(Config("proxy-2", "8.8.8.8"));
        var other = CoreSwitchPlanner.ReadSingboxShape(Config("proxy-2", "1.1.1.1"));

        a.Should().NotBeNull();
        a!.SelectedTag.Should().Be("proxy-1");
        b!.SelectedTag.Should().Be("proxy-2");
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
