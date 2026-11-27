namespace ServiceLib.Tests.Common;

// Inside the namespace: this scope is searched first, so Should() binds to AwesomeAssertions, not to TUnit's global Should().
using AwesomeAssertions;

public class SvoRayWindowLayoutTests
{
    // A 1920x1080 screen with the taskbar at the bottom.
    private const double WorkLeft = 0, WorkTop = 0, WorkWidth = 1920, WorkHeight = 1032;

    [Test]
    public void For_ShouldCentreTheDefaultShapeOfEachMode()
    {
        var simple = SvoRayWindowLayout.For(true, null, null, WorkLeft, WorkTop, WorkWidth, WorkHeight);
        simple.Should().Be(new SvoRayWindowBounds(750, 126, 420, 780, 360, 560, 560));

        var advanced = SvoRayWindowLayout.For(false, null, null, WorkLeft, WorkTop, WorkWidth, WorkHeight);
        advanced.Should().Be(new SvoRayWindowBounds(370, 136, 1180, 760, 860, 620, double.PositiveInfinity));
    }

    [Test]
    public void For_ShouldKeepTheSizeTheUserLeftEachModeAt()
    {
        var advanced = SvoRayWindowLayout.For(false, 1400, 900, WorkLeft, WorkTop, WorkWidth, WorkHeight);

        advanced.Width.Should().Be(1400);
        advanced.Height.Should().Be(900);
        advanced.Left.Should().Be(260);
        advanced.Top.Should().Be(66);
    }

    [Test]
    public void For_ShouldHoldEachModeToItsLimits()
    {
        // A desktop-sized value never reaches simple mode, and a phone-sized one never advanced mode.
        var simple = SvoRayWindowLayout.For(true, 1400, 300, WorkLeft, WorkTop, WorkWidth, WorkHeight);
        simple.Width.Should().Be(SvoRayWindowLayout.SimpleMaxWidth);
        simple.Height.Should().Be(SvoRayWindowLayout.SimpleMinHeight);

        var advanced = SvoRayWindowLayout.For(false, 400, 500, WorkLeft, WorkTop, WorkWidth, WorkHeight);
        advanced.Width.Should().Be(SvoRayWindowLayout.AdvancedMinWidth);
        advanced.Height.Should().Be(SvoRayWindowLayout.AdvancedMinHeight);
    }

    [Test]
    public void For_ShouldFitASmallScreenAndItsOffset()
    {
        // A 1366x728 work area on a second monitor to the right of the first.
        var advanced = SvoRayWindowLayout.For(false, null, null, 1920, 0, 1366, 728);

        advanced.Width.Should().Be(1180);
        advanced.Height.Should().Be(728);
        advanced.Left.Should().Be(1920 + 93);
        advanced.Top.Should().Be(0);
    }

    [Test]
    public void RelaxedLimits_ShouldAdmitBothShapes()
    {
        // Set during the switch, they must let either final size through without clamping it.
        SvoRayWindowLayout.RelaxedMinWidth.Should().BeLessThanOrEqualTo(SvoRayWindowLayout.SimpleMinWidth);
        SvoRayWindowLayout.RelaxedMinHeight.Should().BeLessThanOrEqualTo(SvoRayWindowLayout.SimpleMinHeight);
        SvoRayWindowLayout.RelaxedMinWidth.Should().BeLessThanOrEqualTo(SvoRayWindowLayout.AdvancedMinWidth);
        SvoRayWindowLayout.RelaxedMinHeight.Should().BeLessThanOrEqualTo(SvoRayWindowLayout.AdvancedMinHeight);
    }
}
