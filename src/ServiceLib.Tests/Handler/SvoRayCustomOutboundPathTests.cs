using ServiceLib.Common;
using ServiceLib.Tests.CoreConfig;

namespace ServiceLib.Tests.Handler;

// Inside the namespace: this scope is searched first, so Should() binds to AwesomeAssertions, not to TUnit's global Should().
using AwesomeAssertions;

/// <summary>
/// A custom outbound (upstream 7.25) names a file in its address. SvoRay runs elevated, so a share
/// or device path must never be opened - not for the core config, not for an export.
/// </summary>
[NotInParallel(nameof(SvoRayCustomOutboundPathTests))]
public class SvoRayCustomOutboundPathTests
{
    // .invalid never resolves; nothing may try.
    private const string SharePath = @"\\svoray-test.invalid\share\outbound.json";
    private const string Outbound = """{ "protocol": "socks", "tag": "proxy", "settings": { "address": "127.0.0.1", "port": 1080 } }""";

    [Test]
    [Arguments(SharePath)]
    [Arguments(@"\\?\UNC\svoray-test.invalid\share\outbound.json")]
    [Arguments(@"\\.\pipe\outbound")]
    [Arguments("")]
    [Arguments(null)]
    public void ResolveCustomOutboundFile_ShouldRefuseWhatIsNotALocalFile(string? address)
    {
        SvoRayPaths.ResolveCustomOutboundFile(address).Should().BeNull();
    }

    [Test]
    public void ResolveCustomOutboundFile_ShouldFindAFileInTheConfigFolderByName()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }
        using var file = ConfigFolderFile();

        SvoRayPaths.ResolveCustomOutboundFile(file.Name).Should().Be(file.FullPath);
        SvoRayPaths.ResolveCustomOutboundFile(file.FullPath).Should().Be(file.FullPath);
        SvoRayPaths.ResolveCustomOutboundFile("missing-" + file.Name).Should().BeNull();
    }

    [Test]
    public async Task ResolveNodeAsync_ShouldRefuseAShareOrDevicePathAndReadAFileInTheConfigFolder()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }
        var config = CoreConfigTestFactory.CreateConfig();
        CoreConfigTestFactory.BindAppManagerConfig(config);

        var share = CoreConfigTestFactory.CreateCustomOutboundNode(ECoreType.Xray, "outbound-share", "share", SharePath);
        var shareContext = CoreConfigTestFactory.CreateContext(config, share, ECoreType.Xray);
        var (_, shareResult) = await CoreConfigContextBuilder.ResolveNodeAsync(shareContext, share, false);

        shareResult.Success.Should().BeFalse();
        shareResult.Errors.Should().ContainSingle().Which.Should().Contain(SharePath);
        shareContext.CustomOutboundContent.Should().NotContainKey(share.IndexId);

        using var file = ConfigFolderFile();
        var device = CoreConfigTestFactory.CreateCustomOutboundNode(ECoreType.Xray, "outbound-device", "device", @"\\?\" + file.FullPath);
        var deviceContext = CoreConfigTestFactory.CreateContext(config, device, ECoreType.Xray);
        var (_, deviceResult) = await CoreConfigContextBuilder.ResolveNodeAsync(deviceContext, device, false);
        deviceResult.Success.Should().BeFalse();
        deviceContext.CustomOutboundContent.Should().NotContainKey(device.IndexId);

        var local = CoreConfigTestFactory.CreateCustomOutboundNode(ECoreType.Xray, "outbound-local", "local", file.Name);
        var localContext = CoreConfigTestFactory.CreateContext(config, local, ECoreType.Xray);
        var (_, localResult) = await CoreConfigContextBuilder.ResolveNodeAsync(localContext, local, false);

        localResult.Errors.Should().BeEmpty();
        localContext.CustomOutboundContent[local.IndexId].Should().Be(Outbound);
    }

    [Test]
    public void ToUri_ShouldNotExportAShareOrDevicePathButExportALocalFile()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }
        var share = CoreConfigTestFactory.CreateCustomOutboundNode(ECoreType.Xray, "outbound-share", "share", SharePath);
        InnerFmt.ToUri([share]).Should().BeNull();

        using var file = ConfigFolderFile();
        // An existing local file behind a device path: opening it would work, so only the guard refuses it.
        var device = CoreConfigTestFactory.CreateCustomOutboundNode(ECoreType.Xray, "outbound-device", "device", @"\\?\" + file.FullPath);
        InnerFmt.ToUri([device]).Should().BeNull();

        var local = CoreConfigTestFactory.CreateCustomOutboundNode(ECoreType.Xray, "outbound-local", "local", file.Name);
        InnerFmt.ToUri([local]).Should().NotBeNullOrEmpty();
    }

    private static TempConfigFile ConfigFolderFile()
    {
        var name = $"svoray-test-{Guid.NewGuid():N}.json";
        var fullPath = Path.GetFullPath(Utils.GetConfigPath(name));
        // The same rule the code under test applies: only a local file is written.
        if (!SvoRayPaths.IsLocalPath(fullPath))
        {
            throw new InvalidOperationException($"The config folder is not on a local drive: {fullPath}");
        }
        File.WriteAllText(fullPath, Outbound);
        return new TempConfigFile(name, fullPath);
    }

    private sealed record TempConfigFile(string Name, string FullPath) : IDisposable
    {
        public void Dispose() => File.Delete(FullPath);
    }
}
