using AwesomeAssertions;
using Xunit;

namespace ServiceLib.Tests.Common;

public sealed class SvoRayGeoFileRefreshTests : IDisposable
{
    private const string Url = "https://cdn.example/geosite.dat";
    // Under the test's own folder: a fixed subfolder per test instance, removed afterwards.
    private readonly string _dir = Path.Combine(AppContext.BaseDirectory, "geo-refresh-tests", Guid.NewGuid().ToString("N"));
    private readonly string _path;

    public SvoRayGeoFileRefreshTests()
    {
        Directory.CreateDirectory(_dir);
        _path = Path.Combine(_dir, SvoRayServerRouting.GeoSiteFileName);
    }

    public void Dispose()
    {
        Directory.Delete(_dir, true);
    }

    [Fact]
    public void IsGeoFileStale_AfterAboutADay()
    {
        var now = new DateTime(2026, 11, 26, 12, 0, 0, DateTimeKind.Utc);

        SvoRayServerRouting.IsGeoFileStale(now.AddHours(-1), now).Should().BeFalse();
        SvoRayServerRouting.IsGeoFileStale(now.AddHours(-19), now).Should().BeFalse();
        SvoRayServerRouting.IsGeoFileStale(now.AddHours(-20), now).Should().BeTrue();
        SvoRayServerRouting.IsGeoFileStale(now.AddDays(-3), now).Should().BeTrue();
        SvoRayServerRouting.IsGeoFileStale(now.AddDays(5), now).Should().BeTrue("written under a wrong clock");
    }

    [Fact]
    public async Task FreshFile_IsNotDownloadedAgain()
    {
        WriteCurrent(["OLD"], DateTime.UtcNow.AddHours(-2));
        var downloads = 0;

        var refresh = await SvoRayServerRouting.EnsureGeoFileAsync(Url, _path, _ =>
        {
            downloads++;
            return Task.FromResult<byte[]?>(Geo("NEW"));
        });

        refresh.Should().BeNull();
        downloads.Should().Be(0);
    }

    [Fact]
    public async Task StaleFile_IsReplacedInTheBackground()
    {
        WriteCurrent(["OLD"], DateTime.UtcNow.AddDays(-2));
        var release = new TaskCompletionSource<byte[]?>();

        var refresh = await SvoRayServerRouting.EnsureGeoFileAsync(Url, _path, _ => release.Task);

        // The caller (a connect) got its answer while the download is still running.
        refresh.Should().NotBeNull();
        SvoRayServerRouting.ReadGeoTags(_path).Should().BeEquivalentTo(["OLD"]);

        release.SetResult(Geo("NEW", "category-ru"));
        await refresh!;
        SvoRayServerRouting.ReadGeoTags(_path).Should().BeEquivalentTo(["NEW", "category-ru"]);
        SvoRayServerRouting.IsGeoFileStale(File.GetLastWriteTimeUtc(_path), DateTime.UtcNow).Should().BeFalse();
        Directory.GetFiles(_dir, "*.tmp").Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)] // the download failed
    [InlineData(true)] // the provider answered with something that is not a geo file
    public async Task StaleFile_IsKeptWhenTheNewOneIsUnusable(bool answered)
    {
        WriteCurrent(["OLD"], DateTime.UtcNow.AddDays(-2));
        byte[]? answer = answered ? Encoding.UTF8.GetBytes("<html>blocked</html>") : null;

        var refresh = await SvoRayServerRouting.EnsureGeoFileAsync(Url, _path, _ => Task.FromResult(answer));
        await refresh!;

        SvoRayServerRouting.ReadGeoTags(_path).Should().BeEquivalentTo(["OLD"]);
        Directory.GetFiles(_dir, "*.tmp").Should().BeEmpty();

        // Not tried again at every connect after a failure.
        (await SvoRayServerRouting.EnsureGeoFileAsync(Url, _path, _ => Task.FromResult(answer))).Should().BeNull();
    }

    [Fact]
    public async Task MissingFile_IsDownloadedBeforeTheRulesAreBuilt()
    {
        var refresh = await SvoRayServerRouting.EnsureGeoFileAsync(Url, _path, _ => Task.FromResult<byte[]?>(Geo("NEW")));

        refresh.Should().BeNull("the caller waited for it");
        SvoRayServerRouting.ReadGeoTags(_path).Should().BeEquivalentTo(["NEW"]);
        File.ReadAllText(_path + ".url").Should().Be(Url);
    }

    private void WriteCurrent(string[] tags, DateTime writtenUtc)
    {
        File.WriteAllBytes(_path, Geo(tags));
        File.WriteAllText(_path + ".url", Url);
        File.SetLastWriteTimeUtc(_path, writtenUtc);
    }

    private static byte[] Geo(params string[] tags)
    {
        var data = new List<byte>();
        foreach (var tag in tags)
        {
            var name = Encoding.UTF8.GetBytes(tag);
            var body = new List<byte> { 0x0A, (byte)name.Length };
            body.AddRange(name);
            data.Add(0x0A);
            data.Add((byte)body.Count);
            data.AddRange(body);
        }
        return [.. data];
    }
}
