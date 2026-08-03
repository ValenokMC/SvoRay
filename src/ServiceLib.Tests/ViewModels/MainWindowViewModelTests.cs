using AwesomeAssertions;
using ServiceLib.Enums;
using ServiceLib.Models;
using ServiceLib.ViewModels;
using Xunit;

namespace ServiceLib.Tests.ViewModels;

public class MainWindowViewModelTests
{
    private static Config CreateConfig(ESvoRayMode mode, ESysProxyType proxyType, bool enableTun)
    {
        return new Config
        {
            SvoRayItem = new SvoRayItem { Mode = mode },
            SystemProxyItem = new SystemProxyItem { SysProxyType = proxyType },
            TunModeItem = new TunModeItem { EnableTun = enableTun }
        };
    }

    [Theory]
    [InlineData(ESvoRayMode.Proxy)]
    [InlineData(ESvoRayMode.Tun)]
    public void IsCoreWanted_Disconnected_ShouldNotWantCore(ESvoRayMode mode)
    {
        // What the off switch leaves behind, in either mode: no tunnel and a cleared system proxy.
        // The core must not run there - it opens its own connections to the endpoint, and the
        // server would go on listing the account as online with the VPN visibly off.
        var config = CreateConfig(mode, ESysProxyType.ForcedClear, enableTun: false);

        MainWindowViewModel.IsCoreWanted(config).Should().BeFalse();
    }

    [Fact]
    public void IsCoreWanted_TunUp_ShouldWantCore()
    {
        var config = CreateConfig(ESvoRayMode.Tun, ESysProxyType.ForcedClear, enableTun: true);

        MainWindowViewModel.IsCoreWanted(config).Should().BeTrue();
    }

    [Theory]
    [InlineData(ESysProxyType.ForcedChange)]
    [InlineData(ESysProxyType.Unchanged)]
    [InlineData(ESysProxyType.Pac)]
    public void IsCoreWanted_SystemProxyNotCleared_ShouldWantCore(ESysProxyType proxyType)
    {
        // Simple mode only ever sets ForcedChange, but advanced mode keeps the v2rayN meanings:
        // "do not change" and PAC are ordinary ways to use the local proxy port by hand, so
        // neither of them may be read as a disconnect.
        var config = CreateConfig(ESvoRayMode.Proxy, proxyType, enableTun: false);

        MainWindowViewModel.IsCoreWanted(config).Should().BeTrue();
    }

    [Theory]
    [InlineData(ESvoRayMode.Proxy, ESysProxyType.ForcedChange, false, true)]
    [InlineData(ESvoRayMode.Proxy, ESysProxyType.ForcedClear, false, false)]
    [InlineData(ESvoRayMode.Proxy, ESysProxyType.Unchanged, false, false)]
    [InlineData(ESvoRayMode.Tun, ESysProxyType.ForcedClear, true, true)]
    [InlineData(ESvoRayMode.Tun, ESysProxyType.ForcedClear, false, false)]
    public void IsSvoRayConnectionRequested_FollowsTheSwitchTheModeUses(
        ESvoRayMode mode, ESysProxyType proxyType, bool enableTun, bool expected)
    {
        var config = CreateConfig(mode, proxyType, enableTun);

        MainWindowViewModel.IsSvoRayConnectionRequested(config).Should().Be(expected);
    }
}
