namespace ServiceLib.Services;

public enum EXraySwapResult
{
    /// <summary>The running core now sends proxy traffic to the new server.</summary>
    Swapped,

    /// <summary>Nothing changed in the running core; it still uses the old server.</summary>
    Unchanged,

    /// <summary>The old proxy outbound is gone and the new one was not added: the core must be restarted.</summary>
    ProxyMissing,
}

/// <summary>
/// Replaces the proxy outbound of a running Xray core through its handler API, using the
/// <c>xray api rmo</c> / <c>xray api ado</c> commands of the core binary itself: they turn the
/// JSON outbound into the core's own format, which would otherwise mean reimplementing Xray's
/// config builder.
///
/// Xray refuses to add an outbound under a tag that exists, so the old one is removed first and
/// routing has no proxy until the new one is in: new connections in that window are dropped. To
/// keep the window short, both commands are started before anything is removed and wait for
/// their JSON on stdin; the removal and the addition are then two loopback calls back to back,
/// without a process start in between. Connections already open on the old server are left to
/// finish on it - the API cannot close them.
/// </summary>
public static class XrayApiService
{
    public const string ApiTag = "api";
    public const string HandlerService = "HandlerService";

    private const string _tag = "XrayApiService";

    /// <param name="xrayExe">The Xray binary, the same one that runs the core.</param>
    /// <param name="apiPort">Loopback port of the running core's handler API.</param>
    /// <param name="oldOutboundJson">The running proxy outbound, as generated.</param>
    /// <param name="newOutboundJson">The proxy outbound to put in its place, with the same tag.</param>
    public static async Task<EXraySwapResult> SwapOutboundAsync(string xrayExe, int apiPort,
        string oldOutboundJson, string newOutboundJson, Action<string>? mark = null)
    {
        Process? remove = null;
        Process? add = null;
        try
        {
            remove = Start(xrayExe, "rmo", apiPort);
            add = Start(xrayExe, "ado", apiPort);
            mark?.Invoke("xray api commands started");

            mark?.Invoke("removing old proxy outbound");
            if (!await FeedAndWaitAsync(remove, WrapOutbound(oldOutboundJson)))
            {
                return EXraySwapResult.Unchanged;
            }

            if (!await FeedAndWaitAsync(add, WrapOutbound(newOutboundJson)))
            {
                return EXraySwapResult.ProxyMissing;
            }
            return EXraySwapResult.Swapped;
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
            // Unknown how far it got: only a restart is sure to leave a working proxy.
            return EXraySwapResult.ProxyMissing;
        }
        finally
        {
            Discard(remove);
            Discard(add);
        }
    }

    /// <summary>
    /// The commands read a whole Xray config and take its <c>outbounds</c>; a bare outbound
    /// object would be read as an empty config.
    /// </summary>
    public static string WrapOutbound(string outboundJson)
    {
        var outbound = JsonNode.Parse(outboundJson);
        return new JsonObject { ["outbounds"] = new JsonArray(outbound) }.ToJsonString();
    }

    private static Process Start(string xrayExe, string command, int apiPort)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = xrayExe,
                // "stdin:" is the commands' own way to read the config from standard input.
                Arguments = $"api {command} -s {Global.Loopback}:{apiPort} -t 3 stdin:",
                // An empty folder of our own: rmo reads any argument that names an existing file.
                WorkingDirectory = Utils.GetTempPath(),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                // Go's JSON reader rejects a byte order mark.
                StandardInputEncoding = new UTF8Encoding(false),
            },
        };
        process.Start();
        return process;
    }

    private static async Task<bool> FeedAndWaitAsync(Process process, string json)
    {
        await process.StandardInput.WriteAsync(json);
        process.StandardInput.Close();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        if (process.ExitCode != 0)
        {
            // Only the code: the command's own message may quote the outbound, server included.
            Logging.SaveLog($"{_tag}: xray api {process.StartInfo.Arguments.Split(' ')[1]} exited with {process.ExitCode}");
            return false;
        }
        return true;
    }

    private static void Discard(Process? process)
    {
        if (process is null)
        {
            return;
        }
        try
        {
            if (!process.HasExited)
            {
                process.Kill();
            }
        }
        catch
        {
            // Already gone.
        }
        process.Dispose();
    }
}
