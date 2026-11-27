namespace ServiceLib.Tests.Common;

// Inside the namespace: this scope is searched first, so Should() binds to AwesomeAssertions, not to TUnit's global Should().
using AwesomeAssertions;

[NotInParallel(nameof(SvoRaySupportUrlTests))]
public class SvoRaySupportUrlTests
{
    [Test]
    [Arguments("https://example.com/support")]
    [Arguments("http://127.0.0.1:8080/help")]
    [Arguments("tg://resolve?domain=svoray_support")]
    public void Normalize_ShouldAllowSupportedSchemes(string value)
    {
        SvoRaySupportUrl.Normalize(value).Should().Be(value);
    }

    [Test]
    [Arguments("javascript:alert(1)")]
    [Arguments("file:///C:/Windows/System32/calc.exe")]
    [Arguments("data:text/html,unsafe")]
    [Arguments("shell:AppsFolder\\unsafe")]
    [Arguments("tg:resolve?domain=missing_slashes")]
    [Arguments("https:///missing-host")]
    public void Resolve_ShouldUseProjectIssuesForUnsafeValues(string value)
    {
        SvoRaySupportUrl.Resolve(value).Should().Be(SvoRaySupportUrl.FallbackUrl);
    }

    [Test]
    public void Resolve_ShouldRejectUnreasonablyLongValues()
    {
        var value = "https://example.com/" + new string('a', SvoRaySupportUrl.MaxLength);

        SvoRaySupportUrl.Resolve(value).Should().Be(SvoRaySupportUrl.FallbackUrl);
    }
}
