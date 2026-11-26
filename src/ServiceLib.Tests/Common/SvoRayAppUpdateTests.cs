using System.Net;
using System.Security.Cryptography;
using System.Text;
using ServiceLib.Common;
using ServiceLib.Services;

namespace ServiceLib.Tests.Common;

// Inside the namespace: this scope is searched first, so Should() binds to AwesomeAssertions, not to TUnit's global Should().
using AwesomeAssertions;

[NotInParallel(nameof(SvoRayAppUpdateTests))]
public class SvoRayAppUpdateTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 26, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public void ShouldOffer_ShouldAskAboutAVersionNotOfferedBefore()
    {
        SvoRayAppUpdate.ShouldOffer(new Version(0, 6, 5), null, 0, Now).Should().BeTrue();
        SvoRayAppUpdate.ShouldOffer(new Version(0, 6, 6), "0.6.5", Now.ToUnixTimeSeconds(), Now).Should().BeTrue();
    }

    [Test]
    public void ShouldOffer_ShouldHoldBackAVersionPutOffWithLaterForADay()
    {
        var offeredAt = Now.AddHours(-6).ToUnixTimeSeconds();

        SvoRayAppUpdate.ShouldOffer(new Version(0, 6, 5), "0.6.5", offeredAt, Now).Should().BeFalse();
        SvoRayAppUpdate.ShouldOffer(new Version(0, 6, 5), "0.6.5", offeredAt, Now.AddHours(19)).Should().BeTrue();
    }

    private static readonly Version Current = new(0, 5, 0, 0);

    /// <summary>The shape of a real <c>/releases/latest</c> answer (v0.4.0), trimmed to what is read.</summary>
    private static string Release(string tag, bool prerelease = false, bool draft = false, string? setupName = null,
        bool withSums = true, string repository = "ValenokMC/SvoRay")
    {
        var version = tag.TrimStart('v');
        setupName ??= $"SvoRay-{version}-setup.exe";
        var assets = new List<string>
        {
            Asset(setupName, repository, tag),
            Asset($"SvoRay-{version}-win-x64.zip", repository, tag),
        };
        if (withSums)
        {
            assets.Add(Asset("SHA256SUMS.txt", repository, tag));
        }
        return $$"""
            {
              "html_url": "https://github.com/ValenokMC/SvoRay/releases/tag/{{tag}}",
              "tag_name": "{{tag}}",
              "draft": {{(draft ? "true" : "false")}},
              "prerelease": {{(prerelease ? "true" : "false")}},
              "assets": [{{string.Join(",", assets)}}]
            }
            """;

        static string Asset(string name, string repository, string tag) => $$"""
            { "name": "{{name}}", "browser_download_url": "https://github.com/{{repository}}/releases/download/{{tag}}/{{name}}" }
            """;
    }

    [Test]
    public void Evaluate_ShouldOfferANewerStableRelease()
    {
        var check = SvoRayAppUpdate.Evaluate(Release("v0.6.0"), Current);

        check.Status.Should().Be(ESvoRayUpdateStatus.Available);
        check.Release!.Version.Should().Be(new Version(0, 6, 0));
        check.Release.SetupName.Should().Be("SvoRay-0.6.0-setup.exe");
        check.Release.SetupUrl.Should().Be(new Uri("https://github.com/ValenokMC/SvoRay/releases/download/v0.6.0/SvoRay-0.6.0-setup.exe"));
        check.Release.SumsUrl.Should().Be(new Uri("https://github.com/ValenokMC/SvoRay/releases/download/v0.6.0/SHA256SUMS.txt"));
        check.Release.NotesUrl.Should().Be("https://github.com/ValenokMC/SvoRay/releases/tag/v0.6.0");
    }

    [Test]
    [Arguments("v0.5.0")]
    [Arguments("v0.4.0")]
    [Arguments("0.4.9")]
    public void Evaluate_ShouldReportUpToDateForAnEqualOrOlderRelease(string tag)
    {
        SvoRayAppUpdate.Evaluate(Release(tag), Current).Status.Should().Be(ESvoRayUpdateStatus.UpToDate);
    }

    [Test]
    public void Evaluate_ShouldCompareVersionsAsNumbersNotStrings()
    {
        // As strings "0.10.0" sorts before "0.9.0".
        SvoRayAppUpdate.Evaluate(Release("v0.10.0"), new Version(0, 9, 0)).Status.Should().Be(ESvoRayUpdateStatus.Available);
        SvoRayAppUpdate.Evaluate(Release("v0.9.0"), new Version(0, 10, 0)).Status.Should().Be(ESvoRayUpdateStatus.UpToDate);
    }

    [Test]
    [Arguments(true, false)]
    [Arguments(false, true)]
    public void Evaluate_ShouldSkipPrereleasesAndDrafts(bool prerelease, bool draft)
    {
        SvoRayAppUpdate.Evaluate(Release("v0.6.0", prerelease, draft), Current).Status.Should().Be(ESvoRayUpdateStatus.Unusable);
    }

    [Test]
    [Arguments("v0.6.0-beta")]
    [Arguments("latest")]
    [Arguments("")]
    public void Evaluate_ShouldRejectTagsThatAreNotPlainVersions(string tag)
    {
        SvoRayAppUpdate.Evaluate(Release(tag), Current).Status.Should().Be(ESvoRayUpdateStatus.Unusable);
    }

    [Test]
    public void Evaluate_ShouldRefuseAReleaseWithoutTheInstallerOrChecksums()
    {
        SvoRayAppUpdate.Evaluate(Release("v0.6.0", setupName: "SvoRay-0.6.0-portable.exe"), Current)
            .Status.Should().Be(ESvoRayUpdateStatus.Unusable);
        SvoRayAppUpdate.Evaluate(Release("v0.6.0", withSums: false), Current)
            .Status.Should().Be(ESvoRayUpdateStatus.Unusable);
    }

    [Test]
    public void Evaluate_ShouldRefuseAssetsServedFromAnotherRepository()
    {
        SvoRayAppUpdate.Evaluate(Release("v0.6.0", repository: "2dust/v2rayN"), Current)
            .Status.Should().Be(ESvoRayUpdateStatus.Unusable);
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("not json")]
    [Arguments("[]")]
    [Arguments("""{ "message": "API rate limit exceeded" }""")]
    public void Evaluate_ShouldTreatOddAnswersAsUnusable(string? json)
    {
        SvoRayAppUpdate.Evaluate(json, Current).Status.Should().Be(ESvoRayUpdateStatus.Unusable);
    }

    [Test]
    [Arguments("v0.5.0", 0, 5, 0)]
    [Arguments("0.5", 0, 5, 0)]
    [Arguments("1.2.3.4", 1, 2, 3)]
    public void ParseVersion_ShouldReadReleaseTags(string tag, int major, int minor, int build)
    {
        SvoRayAppUpdate.ParseVersion(tag).Should().Be(new Version(major, minor, build));
    }

    [Test]
    public void FindExpectedHash_ShouldReadTheSha256SumsFormat()
    {
        var hash = new string('A', 64);
        var sums = $"{new string('1', 64)}  SvoRay-0.6.0-win-x64.zip\r\n{hash}  SvoRay-0.6.0-setup.exe\r\n";

        SvoRayAppUpdate.FindExpectedHash(sums, "SvoRay-0.6.0-setup.exe").Should().Be(hash.ToLowerInvariant());
        SvoRayAppUpdate.FindExpectedHash($"{hash} *SvoRay-0.6.0-setup.exe", "SvoRay-0.6.0-setup.exe").Should().NotBeNull();
        SvoRayAppUpdate.FindExpectedHash(sums, "SvoRay-0.6.0-source.zip").Should().BeNull();
        SvoRayAppUpdate.FindExpectedHash("xyz  SvoRay-0.6.0-setup.exe", "SvoRay-0.6.0-setup.exe").Should().BeNull();
    }

    [Test]
    public async Task DownloadAsync_ShouldReturnTheInstallerWhenItsHashMatches()
    {
        var installer = Encoding.UTF8.GetBytes("installer body");
        using var folder = new TempFolder();
        var service = Service(installer, Sums(installer), folder.Path);

        var check = (await service.CheckAsync(Current))!;
        using var verified = await service.DownloadAsync(check.Release!, null);

        File.ReadAllBytes(verified.Path).Should().Equal(installer);
        // Held open against writes until it is started.
        var write = () => new FileStream(verified.Path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite).Dispose();
        write.Should().Throw<IOException>();
    }

    [Test]
    public async Task DownloadAsync_ShouldRefuseAndDeleteAnInstallerWithAnotherHash()
    {
        using var folder = new TempFolder();
        var service = Service(Encoding.UTF8.GetBytes("tampered"), Sums(Encoding.UTF8.GetBytes("original")), folder.Path);

        var check = (await service.CheckAsync(Current))!;
        var download = () => service.DownloadAsync(check.Release!, null);

        (await download.Should().ThrowAsync<SvoRayUpdateException>()).Which.Failure.Should().Be(ESvoRayUpdateFailure.HashMismatch);
        Directory.GetFiles(folder.Path).Should().BeEmpty();
    }

    [Test]
    public async Task CheckAsync_ShouldFallBackToTheNextRouteAndReturnNullWhenAllFail()
    {
        var proxy = new WebProxy("socks5://127.0.0.1:1");
        var seen = new List<IWebProxy?>();
        var fallback = new SvoRayUpdateService(
            () => Task.FromResult<IReadOnlyList<IWebProxy?>>([proxy, null]),
            route =>
            {
                seen.Add(route);
                return new FakeHandler(route is null ? Respond(Release("v0.6.0")) : _ => throw new HttpRequestException("blocked"));
            },
            Path.GetTempPath());

        (await fallback.CheckAsync(Current))!.Status.Should().Be(ESvoRayUpdateStatus.Available);
        seen.Should().Equal(proxy, null);

        var offline = new SvoRayUpdateService(
            () => Task.FromResult<IReadOnlyList<IWebProxy?>>([null]),
            _ => new FakeHandler(_ => throw new HttpRequestException("offline")),
            Path.GetTempPath());
        (await offline.CheckAsync(Current)).Should().BeNull();
    }

    private static string Sums(byte[] installer)
    {
        return $"{Convert.ToHexStringLower(SHA256.HashData(installer))}  SvoRay-0.6.0-setup.exe\n";
    }

    private static SvoRayUpdateService Service(byte[] installer, string sums, string folder)
    {
        var routes = () => Task.FromResult<IReadOnlyList<IWebProxy?>>([null]);
        return new SvoRayUpdateService(routes, _ => new FakeHandler(request => request.RequestUri!.AbsoluteUri switch
        {
            SvoRayAppUpdate.LatestReleaseApiUrl => Text(Release("v0.6.0")),
            var url when url.EndsWith("/SHA256SUMS.txt", StringComparison.Ordinal) => Text(sums),
            var url when url.EndsWith("/SvoRay-0.6.0-setup.exe", StringComparison.Ordinal) => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(installer)
            },
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        }), folder);
    }

    private static Func<HttpRequestMessage, HttpResponseMessage> Respond(string body) => _ => Text(body);

    private static HttpResponseMessage Text(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body) };

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(respond(request));
        }
    }

    private sealed class TempFolder : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("svoray-update-").FullName;

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, true);
            }
            catch (IOException)
            {
            }
        }
    }
}
