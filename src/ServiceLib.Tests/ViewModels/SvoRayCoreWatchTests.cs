using AwesomeAssertions;
using ServiceLib.Enums;
using ServiceLib.ViewModels;
using Xunit;

namespace ServiceLib.Tests.ViewModels;

public class SvoRayCoreWatchTests
{
    [Fact]
    public void ShouldReportCoreStopped_WhenTheCoreIsGoneUnderAGreenScreen()
    {
        MainWindowViewModel.ShouldReportCoreStopped(
            reloading: false, requested: true, coreRunning: false, shown: ESvoRayConnectionState.On)
            .Should().BeTrue();
    }

    [Theory]
    [InlineData(true, true, false, ESvoRayConnectionState.On)]          // a reload stops it on purpose
    [InlineData(false, false, false, ESvoRayConnectionState.On)]        // the user turned the VPN off
    [InlineData(false, true, true, ESvoRayConnectionState.On)]          // the core is running
    [InlineData(false, true, false, ESvoRayConnectionState.Connecting)] // not up yet
    [InlineData(false, true, false, ESvoRayConnectionState.Error)]      // already reported
    public void ShouldReportCoreStopped_ShouldStayQuietOtherwise(bool reloading, bool requested, bool coreRunning, ESvoRayConnectionState shown)
    {
        MainWindowViewModel.ShouldReportCoreStopped(reloading, requested, coreRunning, shown).Should().BeFalse();
    }
}
