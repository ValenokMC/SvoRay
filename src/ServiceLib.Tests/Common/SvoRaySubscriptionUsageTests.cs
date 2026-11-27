using ServiceLib.Common;
using ServiceLib.Manager;
using ServiceLib.Tests.CoreConfig;

namespace ServiceLib.Tests.Common;

// Inside the namespace: this scope is searched first, so Should() binds to AwesomeAssertions, not to TUnit's global Should().
using AwesomeAssertions;

[NotInParallel(nameof(SvoRaySubscriptionUsageTests))]
public class SvoRaySubscriptionUsageTests
{
    [Test]
    public void Parse_ShouldReadThePanelHeader()
    {
        var usage = SvoRaySubscriptionUsage.Parse("upload=7171579511; download=95274278193; total=214748364800; expire=0");

        usage.Should().NotBeNull();
        usage!.Used.Should().Be(7171579511 + 95274278193);
        usage.Total.Should().Be(214748364800);
        usage.Expire.Should().Be(0);
    }

    [Test]
    public void Parse_ShouldTolerateOrderSpacingAndCase()
    {
        var usage = SvoRaySubscriptionUsage.Parse(" Total=100 ;download = 30;UPLOAD=10 ");

        usage.Should().Be(new SvoRaySubscriptionUsage(10, 30, 100, 0));
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("expire=0")]
    [Arguments("upload=abc; download=-5")]
    public void Parse_ShouldIgnoreHeadersWithoutTraffic(string? header)
    {
        SvoRaySubscriptionUsage.Parse(header).Should().BeNull();
    }

    [Test]
    public void StoredForm_ShouldRoundTrip()
    {
        var usage = new SvoRaySubscriptionUsage(1, 2, 3, 4);

        SvoRaySubscriptionUsage.Parse(usage.Serialize()).Should().Be(usage);
    }

    [Test]
    [Arguments(0, "0 B")]
    [Arguments(1536, "1.5 KB")]
    [Arguments(102_436_000_000, "95.4 GB")]
    [Arguments(214_748_364_800, "200 GB")]
    [Arguments(1_319_413_953_331, "1.2 TB")]
    public void FormatBytes_ShouldPickAReadableUnit(long bytes, string expected)
    {
        SvoRaySubscriptionUsage.FormatBytes(bytes).Replace(',', '.').Should().Be(expected);
    }

    [Test]
    public void FormatBytes_ShouldUseTheUnitsOfTheInterfaceLanguage()
    {
        SvoRaySubscriptionUsage.FormatBytes(214_748_364_800, "Б,КБ,МБ,ГБ,ТБ").Should().Be("200 ГБ");
    }

    /// <summary>
    /// CertPemManager.Init runs late in the main view model's start-up; when anything before it
    /// failed, a null config made every HttpClient download throw and the subscription came
    /// through a fallback that drops response headers. The manager now takes the app config
    /// itself.
    /// </summary>
    [Test]
    public void CertificatePolicy_ShouldNotNeedAnExplicitInit()
    {
        var config = CoreConfigTestFactory.CreateConfig();
        CoreConfigTestFactory.BindAppManagerConfig(config);

        var act = () => CertPemManager.Instance.BuildCertificateChainPolicy();

        act.Should().NotThrow();
    }
}
