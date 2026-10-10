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
}
