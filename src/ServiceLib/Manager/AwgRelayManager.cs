namespace ServiceLib.Manager;

/// <summary>
/// Owns the <see cref="AwgRelay"/> of each WireGuard profile that carries AmneziaWG decoys.
/// Writing a core config asks <see cref="ResolveEndpoint"/> where the core should send its
/// WireGuard traffic: the server itself, or the relay in front of it. A relay asked for again
/// with the same settings is reused, so writing a new config never cuts a running tunnel;
/// <see cref="MarkStale"/> and <see cref="StopStale"/> around a core restart close the relays
/// the new config no longer uses.
/// </summary>
public sealed class AwgRelayManager
{
    private static readonly Lazy<AwgRelayManager> _instance = new(() => new());
    public static AwgRelayManager Instance => _instance.Value;
    private const string _tag = "AwgRelayManager";

    private sealed class Entry(AwgRelay relay)
    {
        public AwgRelay Relay { get; } = relay;
        public bool InUse { get; set; } = true;
    }

    private readonly Lock _lock = new();
    private readonly Dictionary<string, Entry> _relays = [];

    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _relays.Count;
            }
        }
    }

    /// <summary>
    /// Where the core should send <paramref name="node"/>'s WireGuard traffic. Profiles without
    /// decoys, and any relay that cannot start, go straight to the server as before.
    /// </summary>
    /// <param name="sendThrough">The local address the core would leave from, kept for the relay's own socket.</param>
    public (string Address, int Port) ResolveEndpoint(ProfileItem node, string? sendThrough)
    {
        if (node.ConfigType != EConfigType.WireGuard)
        {
            return (node.Address, node.Port);
        }
        var decoys = AwgDecoys.From(node.GetProtocolExtra());
        if (decoys is null)
        {
            return (node.Address, node.Port);
        }

        var key = $"{node.Address}|{node.Port}|{node.ProtoExtra}|{sendThrough}";
        lock (_lock)
        {
            if (!_relays.TryGetValue(key, out var entry))
            {
                try
                {
                    var bindAddress = IPAddress.TryParse(sendThrough, out var address) ? address : null;
                    entry = new Entry(new AwgRelay(node.Address, node.Port, decoys, bindAddress));
                    _relays[key] = entry;
                }
                catch (Exception ex)
                {
                    Logging.SaveLog(_tag, ex);
                    return (node.Address, node.Port);
                }
            }
            entry.InUse = true;
            return (IPAddress.Loopback.ToString(), entry.Relay.LocalPort);
        }
    }

    /// <summary>Before a config is written: every relay counts as unused until asked for again.</summary>
    public void MarkStale()
    {
        lock (_lock)
        {
            foreach (var entry in _relays.Values)
            {
                entry.InUse = false;
            }
        }
    }

    /// <summary>Closes the relays nothing asked for since <see cref="MarkStale"/>.</summary>
    public void StopStale()
    {
        lock (_lock)
        {
            foreach (var (key, entry) in _relays.Where(item => !item.Value.InUse).ToList())
            {
                entry.Relay.Dispose();
                _relays.Remove(key);
            }
        }
    }

    public void StopAll()
    {
        lock (_lock)
        {
            foreach (var entry in _relays.Values)
            {
                entry.Relay.Dispose();
            }
            _relays.Clear();
        }
    }
}
