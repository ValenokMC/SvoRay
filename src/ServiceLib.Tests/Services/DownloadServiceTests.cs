namespace ServiceLib.Tests.Services;

// Inside the namespace: this scope is searched first, so Should() binds to AwesomeAssertions, not to TUnit's global Should().
using AwesomeAssertions;

[NotInParallel(nameof(DownloadServiceTests))]
public class DownloadServiceTests
{
    [Test]
    public async Task FromHttpResponseAsync_ShouldPreserveSupportUrl()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("subscription-data")
        };
        response.Headers.TryAddWithoutValidation("Support-Url", "tg://resolve?domain=svoray_support");
        response.Content.Headers.TryAddWithoutValidation("X-Subscription-Name", "SvoRay test");

        var result = await DownloadStringResult.FromHttpResponseAsync(
            response,
            TestContext.Current!.Execution.CancellationToken);

        result.Content.Should().Be("subscription-data");
        result.GetHeaderValue("support-url").Should().Be("tg://resolve?domain=svoray_support");
        result.GetHeaderValue("x-subscription-name").Should().Be("SvoRay test");
    }
}
