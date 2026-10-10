using AwesomeAssertions;
using ServiceLib.Common;
using ServiceLib.Manager;
using ServiceLib.Tests.CoreConfig;
using Xunit;

namespace ServiceLib.Tests.Common;

public class SvoRaySubscriptionUsageTests
{
    [Fact]
    public void Parse_ShouldReadThePanelHeader()
    {
        var usage = SvoRaySubscriptionUsage.Parse("upload=7171579511; download=95274278193; total=214748364800; expire=0");

        usage.Should().NotBeNull();
        usage!.Used.Should().Be(7171579511 + 95274278193);
        usage.Total.Should().Be(214748364800);
        usage.Expire.Should().Be(0);
    }

    [Fact]
    public void Parse_ShouldTolerateOrderSpacingAndCase()
    {
        var usage = SvoRaySubscriptionUsage.Parse(" Total=100 ;download = 30;UPLOAD=10 ");

        usage.Should().Be(new SvoRaySubscriptionUsage(10, 30, 100, 0));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("expire=0")]
    [InlineData("upload=abc; download=-5")]
    public void Parse_ShouldIgnoreHeadersWithoutTraffic(string? header)
    {
        SvoRaySubscriptionUsage.Parse(header).Should().BeNull();
    }

    [Fact]
    public void StoredForm_ShouldRoundTrip()
    {
        var usage = new SvoRaySubscriptionUsage(1, 2, 3, 4);

        SvoRaySubscriptionUsage.Parse(usage.Serialize()).Should().Be(usage);
    }

    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(102_436_000_000, "95.4 GB")]
    [InlineData(214_748_364_800, "200 GB")]
    [InlineData(1_319_413_953_331, "1.2 TB")]
    public void FormatBytes_ShouldPickAReadableUnit(long bytes, string expected)
    {
        SvoRaySubscriptionUsage.FormatBytes(bytes).Replace(',', '.').Should().Be(expected);
    }

    [Fact]
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
    [Fact]
    public void CertificatePolicy_ShouldNotNeedAnExplicitInit()
    {
        var config = CoreConfigTestFactory.CreateConfig();
        CoreConfigTestFactory.BindAppManagerConfig(config);

        var act = () => CertPemManager.Instance.BuildCertificateChainPolicy();

        act.Should().NotThrow();
    }
}
