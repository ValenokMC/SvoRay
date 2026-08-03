using AwesomeAssertions;
using Xunit;

namespace ServiceLib.Tests.Common;

public class SvoRaySupportUrlTests
{
    [Theory]
    [InlineData("https://example.com/support")]
    [InlineData("http://127.0.0.1:8080/help")]
    [InlineData("tg://resolve?domain=svoray_support")]
    public void Normalize_ShouldAllowSupportedSchemes(string value)
    {
        SvoRaySupportUrl.Normalize(value).Should().Be(value);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///C:/Windows/System32/calc.exe")]
    [InlineData("data:text/html,unsafe")]
    [InlineData("shell:AppsFolder\\unsafe")]
    [InlineData("tg:resolve?domain=missing_slashes")]
    [InlineData("https:///missing-host")]
    public void Resolve_ShouldUseProjectIssuesForUnsafeValues(string value)
    {
        SvoRaySupportUrl.Resolve(value).Should().Be(SvoRaySupportUrl.FallbackUrl);
    }

    [Fact]
    public void Resolve_ShouldRejectUnreasonablyLongValues()
    {
        var value = "https://example.com/" + new string('a', SvoRaySupportUrl.MaxLength);

        SvoRaySupportUrl.Resolve(value).Should().Be(SvoRaySupportUrl.FallbackUrl);
    }
}
