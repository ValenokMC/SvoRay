using System.Net;

namespace ServiceLib.Common;

/// <summary>
/// Measures how long switching the server or reconnecting takes, phase by phase. Every line it
/// writes starts with <see cref="Prefix"/> and carries only phase names, durations, the core type
/// and the mode - never a server name, address or subscription - so the lines can be filtered out
/// of the log and shared without exposing anything else.
/// </summary>
public static class SwitchTiming
{
    public const string Prefix = "[SwitchTiming]";

    private static readonly Lock _lock = new();
    private static Stopwatch? _watch;
    private static long _lastMark;
    private static long _gapStart = -1;
    private static int _id;
    private static bool _finished = true;

    /// <summary>Starts a new measurement, ending any that is still open.</summary>
    public static void Begin(string reason)
    {
        lock (_lock)
        {
            _id++;
            _watch = Stopwatch.StartNew();
            _lastMark = 0;
            _gapStart = -1;
            _finished = false;
            Write($"#{_id} begin: {reason}");
        }
    }

    /// <summary>
    /// Joins the measurement a switch has just started, or starts one when nothing is open:
    /// a reload asked for by something other than a server switch is measured on its own.
    /// </summary>
    public static void Continue(string reason)
    {
        lock (_lock)
        {
            if (_watch is null || _finished || _watch.ElapsedMilliseconds > 10_000)
            {
                Begin(reason);
                return;
            }
        }
        Mark(reason);
    }

    /// <summary>Logs the time spent since the previous mark and since the start.</summary>
    public static void Mark(string phase)
    {
        lock (_lock)
        {
            if (_watch is null || _finished)
            {
                return;
            }
            var now = _watch.ElapsedMilliseconds;
            Write($"#{_id} {phase}: +{now - _lastMark} ms (total {now} ms)");
            _lastMark = now;
        }
    }

    /// <summary>
    /// Marks the moment new traffic stops flowing through the old connection: the old core is
    /// stopped, or the running core is told to use another server.
    /// </summary>
    public static void MarkGapStart(string phase)
    {
        lock (_lock)
        {
            if (_watch is null || _finished)
            {
                return;
            }
            _gapStart = _watch.ElapsedMilliseconds;
        }
        Mark(phase);
    }

    /// <summary>Closes the measurement; later marks are ignored until the next begin.</summary>
    public static void End(string phase)
    {
        Mark(phase);
        lock (_lock)
        {
            _finished = true;
        }
    }

    /// <summary>
    /// Sends requests through the local proxy port until one gets an answer and logs when that
    /// happened: the moment traffic really flows again, which the UI state alone does not tell.
    /// One small request to the configured connectivity-check URL, the same one the availability
    /// check after every reload uses.
    /// </summary>
    public static async Task ProbeFirstTrafficAsync(int socksPort, string? url, int timeoutMs = 20_000)
    {
        int id;
        lock (_lock)
        {
            if (_watch is null || _finished)
            {
                return;
            }
            id = _id;
        }
        if (url.IsNullOrEmpty())
        {
            return;
        }

        var attempts = 0;
        using var overall = new CancellationTokenSource(timeoutMs);
        using var client = new HttpClient(new SocketsHttpHandler
        {
            Proxy = new WebProxy($"socks5://{Global.Loopback}:{socksPort}"),
            UseProxy = true,
            ConnectTimeout = TimeSpan.FromSeconds(2),
            PooledConnectionLifetime = TimeSpan.Zero,
        });
        client.Timeout = TimeSpan.FromSeconds(5);
        while (!overall.IsCancellationRequested)
        {
            attempts++;
            try
            {
                using var response = await client.GetAsync(url, overall.Token).ConfigureAwait(false);
                FinishProbe(id, $"first traffic through proxy ok (attempts {attempts})");
                return;
            }
            catch
            {
                // Not up yet; try again shortly.
            }
            try
            {
                await Task.Delay(100, overall.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
        FinishProbe(id, $"first traffic through proxy FAILED after {timeoutMs} ms (attempts {attempts})");
    }

    private static void FinishProbe(int id, string phase)
    {
        lock (_lock)
        {
            if (_watch is null || id != _id)
            {
                return;
            }
            var now = _watch.ElapsedMilliseconds;
            var gap = _gapStart >= 0 ? $", traffic gap {now - _gapStart} ms" : string.Empty;
            Write($"#{_id} {phase}: total {now} ms{gap}");
            _finished = true;
        }
    }

    private static void Write(string line)
    {
        Logging.SaveLog($"{Prefix} {line}");
    }
}
