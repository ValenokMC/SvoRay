namespace ServiceLib.Services;

public enum ESvoRayUpdateFailure
{
    /// <summary>GitHub could not be reached on any route, or the download broke off.</summary>
    Network,

    /// <summary>The installer does not match the checksum published with the release.</summary>
    HashMismatch,
}

public sealed class SvoRayUpdateException(ESvoRayUpdateFailure failure, string message, Exception? inner = null)
    : Exception(message, inner)
{
    public ESvoRayUpdateFailure Failure { get; } = failure;
}

/// <summary>
/// The downloaded installer, checked against SHA256SUMS.txt and held open with writes denied
/// until it has been started, so nothing can swap the file between the check and the launch.
/// </summary>
public sealed class SvoRayVerifiedInstaller(string path, FileStream lockHandle) : IDisposable
{
    public string Path { get; } = path;

    public void Dispose() => lockHandle.Dispose();
}

/// <summary>
/// Talks to GitHub for <see cref="SvoRayAppUpdate"/>. Each request tries the local proxy of a
/// running core first and then a direct connection, so a check works whether the VPN is on or
/// off and whichever of the two paths happens to be blocked.
/// </summary>
public sealed class SvoRayUpdateService
{
    private const long MaxInstallerBytes = 512L * 1024 * 1024;
    private const int MaxTextBytes = 1024 * 1024;
    private static readonly string _tag = "SvoRayUpdate";

    private readonly Func<Task<IReadOnlyList<IWebProxy?>>> _routes;
    private readonly Func<IWebProxy?, HttpMessageHandler> _handlerFactory;
    private readonly string _downloadFolder;

    public SvoRayUpdateService()
        : this(DefaultRoutesAsync, DefaultHandler, Utils.GetTempPath("update"))
    {
    }

    /// <summary>For tests: fixed routes, a fake handler and a scratch folder.</summary>
    public SvoRayUpdateService(Func<Task<IReadOnlyList<IWebProxy?>>> routes,
        Func<IWebProxy?, HttpMessageHandler> handlerFactory, string downloadFolder)
    {
        _routes = routes;
        _handlerFactory = handlerFactory;
        _downloadFolder = downloadFolder;
    }

    /// <summary>Null when GitHub could not be reached; the failure is only logged.</summary>
    public async Task<SvoRayUpdateCheck?> CheckAsync(Version current, CancellationToken token = default)
    {
        var json = await GetTextAsync(new Uri(SvoRayAppUpdate.LatestReleaseApiUrl), token);
        if (json is null)
        {
            return null;
        }

        var check = SvoRayAppUpdate.Evaluate(json, current);
        if (check.Status == ESvoRayUpdateStatus.Unusable)
        {
            Logging.SaveLog($"{_tag}: latest release not usable ({check.Reason})");
        }
        return check;
    }

    /// <summary>
    /// Downloads the installer and the checksum list of <paramref name="release"/> and returns
    /// the installer only when its SHA-256 matches. A mismatched file is deleted.
    /// </summary>
    public async Task<SvoRayVerifiedInstaller> DownloadAsync(SvoRayRelease release, IProgress<double>? progress,
        CancellationToken token = default)
    {
        var sums = await GetTextAsync(release.SumsUrl, token)
            ?? throw new SvoRayUpdateException(ESvoRayUpdateFailure.Network, "checksums not downloaded");
        var expected = SvoRayAppUpdate.FindExpectedHash(sums, release.SetupName)
            ?? throw new SvoRayUpdateException(ESvoRayUpdateFailure.HashMismatch, "installer not listed in checksums");

        Directory.CreateDirectory(_downloadFolder);
        var path = System.IO.Path.Combine(_downloadFolder, release.SetupName);
        if (!await DownloadFileAsync(release.SetupUrl, path, progress, token))
        {
            TryDelete(path);
            throw new SvoRayUpdateException(ESvoRayUpdateFailure.Network, "installer not downloaded");
        }

        var lockHandle = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (!SvoRayAppUpdate.HashMatches(lockHandle, expected))
        {
            lockHandle.Dispose();
            TryDelete(path);
            throw new SvoRayUpdateException(ESvoRayUpdateFailure.HashMismatch, "installer hash mismatch");
        }
        return new SvoRayVerifiedInstaller(path, lockHandle);
    }

    private async Task<string?> GetTextAsync(Uri uri, CancellationToken token)
    {
        foreach (var proxy in await _routes())
        {
            try
            {
                using var client = CreateClient(proxy, TimeSpan.FromSeconds(20));
                using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token);
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength > MaxTextBytes)
                {
                    return null;
                }
                return await response.Content.ReadAsStringAsync(token);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !token.IsCancellationRequested)
            {
                // The type only: messages can carry addresses, and a failed route is expected.
                Logging.SaveLog($"{_tag}: {(proxy is null ? "direct" : "via proxy")} request failed: {ex.GetType().Name}");
            }
        }
        return null;
    }

    private async Task<bool> DownloadFileAsync(Uri uri, string path, IProgress<double>? progress, CancellationToken token)
    {
        foreach (var proxy in await _routes())
        {
            try
            {
                using var client = CreateClient(proxy, TimeSpan.FromMinutes(15));
                using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token);
                response.EnsureSuccessStatusCode();
                var length = response.Content.Headers.ContentLength;
                if (length > MaxInstallerBytes)
                {
                    return false;
                }

                await using var source = await response.Content.ReadAsStreamAsync(token);
                await using var target = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
                var buffer = new byte[81920];
                long total = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, token)) > 0)
                {
                    total += read;
                    if (total > MaxInstallerBytes)
                    {
                        return false;
                    }
                    await target.WriteAsync(buffer.AsMemory(0, read), token);
                    if (length > 0)
                    {
                        progress?.Report((double)total / length.Value);
                    }
                }
                return true;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !token.IsCancellationRequested)
            {
                Logging.SaveLog($"{_tag}: {(proxy is null ? "direct" : "via proxy")} download failed: {ex.GetType().Name}");
            }
        }
        return false;
    }

    private HttpClient CreateClient(IWebProxy? proxy, TimeSpan timeout)
    {
        var client = new HttpClient(_handlerFactory(proxy)) { Timeout = timeout };
        // GitHub's API refuses requests without a User-Agent.
        client.DefaultRequestHeaders.UserAgent.TryParseAdd($"{Global.AppName}-updater");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    /// <summary>
    /// The core's local SOCKS port when the core is running, then a direct connection. With TUN
    /// on, "direct" still goes through the tunnel; with the VPN off it is the only way out.
    /// </summary>
    private static async Task<IReadOnlyList<IWebProxy?>> DefaultRoutesAsync()
    {
        var routes = new List<IWebProxy?>();
        if (CoreManager.Instance.IsCoreRunning)
        {
            var port = AppManager.Instance.GetLocalPort(EInboundProtocol.socks);
            if (await IsListeningAsync(port))
            {
                routes.Add(new WebProxy($"socks5://{Global.Loopback}:{port}"));
            }
        }
        routes.Add(null);
        return routes;
    }

    private static HttpMessageHandler DefaultHandler(IWebProxy? proxy)
    {
        return new SocketsHttpHandler
        {
            Proxy = proxy,
            // Without a route of our own the system proxy is ignored as well: it may point at
            // a core that has just stopped.
            UseProxy = proxy is not null,
            ConnectTimeout = TimeSpan.FromSeconds(8),
            AllowAutoRedirect = true,
        };
    }

    private static async Task<bool> IsListeningAsync(int port)
    {
        try
        {
            using var client = new TcpClient();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            await client.ConnectAsync(Global.Loopback, port, cts.Token);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex)
        {
            Logging.SaveLog($"{_tag}: could not delete {System.IO.Path.GetFileName(path)}: {ex.GetType().Name}");
        }
    }
}
