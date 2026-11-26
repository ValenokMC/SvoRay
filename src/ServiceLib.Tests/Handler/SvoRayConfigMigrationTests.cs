using AwesomeAssertions;
using ServiceLib.Handler;
using Xunit;

namespace ServiceLib.Tests.Handler;

public class SvoRayConfigMigrationTests
{
    [Fact]
    public void MigrateSvoRayItem_ShouldSwitchAnOlderConfigToTunOnce()
    {
        // A 0.4.x config: the user was on the system proxy, and the flag did not exist yet.
        var config = JsonUtils.Deserialize<Config>("""
            { "GuiItem": {}, "SvoRayItem": { "Mode": 1 } }
            """)!;
        config.SvoRayItem.Mode.Should().Be(ESvoRayMode.Proxy);

        ConfigHandler.MigrateSvoRayItem(config);

        config.SvoRayItem.Mode.Should().Be(ESvoRayMode.Tun);
        config.SvoRayItem.TunDefaultMigrated.Should().BeTrue();
    }

    [Fact]
    public void MigrateSvoRayItem_ShouldKeepProxyChosenAfterTheMigration()
    {
        var config = new Config
        {
            GuiItem = new(),
            SvoRayItem = new() { Mode = ESvoRayMode.Proxy, TunDefaultMigrated = true },
        };

        ConfigHandler.MigrateSvoRayItem(config);

        config.SvoRayItem.Mode.Should().Be(ESvoRayMode.Proxy);
    }

    [Fact]
    public void MigrateSvoRayItem_ShouldMoveHysteriaOffTheOldBrutalDefaultOnce()
    {
        // A 0.6.4 config: v2rayN's 100/100 default, which forced Brutal at 100 Mbps.
        var config = new Config
        {
            GuiItem = new(),
            HysteriaItem = new() { UpMbps = 100, DownMbps = 100 },
            SvoRayItem = new(),
        };

        ConfigHandler.MigrateSvoRayItem(config);

        config.HysteriaItem.UpMbps.Should().Be(0);
        config.HysteriaItem.DownMbps.Should().Be(0);
        config.SvoRayItem.HysteriaBbrMigrated.Should().BeTrue();

        // Set back to 100/100 by the user after the migration: kept on the next start.
        config.HysteriaItem.UpMbps = 100;
        config.HysteriaItem.DownMbps = 100;
        ConfigHandler.MigrateSvoRayItem(config);
        config.HysteriaItem.UpMbps.Should().Be(100);
    }

    [Fact]
    public void MigrateSvoRayItem_ShouldKeepAHysteriaSpeedTheUserTyped()
    {
        var config = new Config
        {
            GuiItem = new(),
            HysteriaItem = new() { UpMbps = 50, DownMbps = 500 },
            SvoRayItem = new(),
        };

        ConfigHandler.MigrateSvoRayItem(config);

        config.HysteriaItem.UpMbps.Should().Be(50);
        config.HysteriaItem.DownMbps.Should().Be(500);
    }
}
