namespace ServiceLib.Handler;

/// <summary>
/// The current VPN server's addresses, and the last ones found for every server name.
/// </summary>
/// <remarks>
/// The name is looked up through Windows, whose DNS the kill switch blocks while it holds - and
/// it holds through a switch between proxy and TUN. A lookup made then fails after a 5-second
/// wait, and the server's address would drop out of both the routes TUN keeps past the tunnel and
/// the kill switch's exceptions: a program that connects to the server directly would be cut off.
/// So the last answer is remembered, and used without asking while the kill switch is up.
/// </remarks>
public static class SvoRayServerAddresses
{
    private static readonly Lock _lock = new();
    private static readonly Dictionary<string, IReadOnlyList<IPAddress>> _known = new(StringComparer.OrdinalIgnoreCase);
    private static IReadOnlyList<IPAddress> _current = [];

    /// <summary>The addresses of the server SvoRay connects to now; empty when unknown.</summary>
    public static IReadOnlyList<IPAddress> Current
    {
        get
        {
            lock (_lock)
            {
                return _current;
            }
        }
    }

    /// <summary>
    /// Finds the addresses of <paramref name="host"/> and makes them <see cref="Current"/>.
    /// </summary>
    /// <param name="lookup">The DNS lookup; it may throw or time out.</param>
    /// <param name="preferKnown">
    /// Use a remembered answer without asking - true while the kill switch blocks the lookup.
    /// </param>
    public static async Task<IReadOnlyList<IPAddress>> ResolveAsync(
        string? host,
        Func<string, Task<IPAddress[]>> lookup,
        bool preferKnown)
    {
        host = host?.Trim();
        if (host.IsNullOrEmpty())
        {
            return SetCurrent([]);
        }
        if (IPAddress.TryParse(host, out var literal))
        {
            return SetCurrent([literal]);
        }

        IReadOnlyList<IPAddress>? known;
        lock (_lock)
        {
            _known.TryGetValue(host!, out known);
        }
        if (preferKnown && known is { Count: > 0 })
        {
            return SetCurrent(known);
        }

        try
        {
            var found = (await lookup(host!))
                .Where(a => a.AddressFamily is AddressFamily.InterNetwork or AddressFamily.InterNetworkV6)
                .Distinct()
                .ToList();
            if (found.Count > 0)
            {
                lock (_lock)
                {
                    _known[host!] = found;
                }
                return SetCurrent(found);
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog($"SvoRay server address: lookup failed, {ex.GetType().Name}");
        }

        return SetCurrent(known ?? []);
    }

    /// <summary>Forgets everything; for tests.</summary>
    public static void Reset()
    {
        lock (_lock)
        {
            _known.Clear();
            _current = [];
        }
    }

    private static IReadOnlyList<IPAddress> SetCurrent(IReadOnlyList<IPAddress> addresses)
    {
        lock (_lock)
        {
            _current = addresses;
        }
        return addresses;
    }
}
