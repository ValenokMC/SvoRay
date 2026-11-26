using ServiceLib.Enums;
using ServiceLib.ViewModels;

namespace ServiceLib.Tests.ViewModels;

// Inside the namespace: this scope is searched first, so Should() binds to AwesomeAssertions, not to TUnit's global Should().
using AwesomeAssertions;

[NotInParallel(nameof(SvoRayCoreWatchTests))]
public class SvoRayCoreWatchTests
{
    [Test]
    public void ShouldReportCoreStopped_WhenTheCoreIsGoneUnderAGreenScreen()
    {
        MainWindowViewModel.ShouldReportCoreStopped(
            reloading: false, requested: true, coreRunning: false, shown: ESvoRayConnectionState.On)
            .Should().BeTrue();
    }

    [Test]
    [Arguments(true, true, false, ESvoRayConnectionState.On)]          // a reload stops it on purpose
    [Arguments(false, false, false, ESvoRayConnectionState.On)]        // the user turned the VPN off
    [Arguments(false, true, true, ESvoRayConnectionState.On)]          // the core is running
    [Arguments(false, true, false, ESvoRayConnectionState.Connecting)] // not up yet
    [Arguments(false, true, false, ESvoRayConnectionState.Error)]      // already reported
    public void ShouldReportCoreStopped_ShouldStayQuietOtherwise(bool reloading, bool requested, bool coreRunning, ESvoRayConnectionState shown)
    {
        MainWindowViewModel.ShouldReportCoreStopped(reloading, requested, coreRunning, shown).Should().BeFalse();
    }
}
