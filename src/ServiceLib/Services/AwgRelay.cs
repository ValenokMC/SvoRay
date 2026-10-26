namespace ServiceLib.Services;

/// <summary>
/// A local UDP relay between a WireGuard core and the real server. The core sends to
/// 127.0.0.1:<see cref="LocalPort"/>; every datagram goes on unchanged in both directions,
/// except that a handshake initiation is preceded by the <see cref="AwgDecoys"/>. This is all
/// a Cloudflare WARP config made for AmneziaWG asks of the client: the server runs plain
/// WireGuard and ignores the decoys, while DPI meets them before the handshake.
/// </summary>
public sealed class AwgRelay : IDisposable
{
    private const string _tag = "AwgRelay";
    private const int MaxSessions = 32;
    // SIO_UDP_CONNRESET: stops an ICMP "port unreachable" from failing the next receive on Windows.
    private const int SioUdpConnReset = -1744830452;

    private readonly string _host;
    private readonly int _port;
    private readonly AwgDecoys _decoys;
    private readonly IPAddress? _bindAddress;
    private readonly Socket _local;
    private readonly CancellationTokenSource _cts = new();
    private readonly ConcurrentDictionary<EndPoint, Socket> _sessions = new();
    private IPEndPoint? _remote;

    public int LocalPort { get; }

    /// <param name="bindAddress">The local address to reach the server from, or null for the system's choice.</param>
    public AwgRelay(string host, int port, AwgDecoys decoys, IPAddress? bindAddress)
    {
        _host = host;
        _port = port;
        _decoys = decoys;
        _bindAddress = bindAddress;

        _local = CreateUdpSocket(AddressFamily.InterNetwork);
        _local.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        LocalPort = ((IPEndPoint)_local.LocalEndPoint!).Port;

        _ = Task.Run(ReceiveLocalAsync);
    }

    public void Dispose()
    {
        if (_cts.IsCancellationRequested)
        {
            return;
        }
        _cts.Cancel();
        _local.Dispose();
        foreach (var session in _sessions.Values)
        {
            session.Dispose();
        }
        _sessions.Clear();
        _cts.Dispose();
    }

    private async Task ReceiveLocalAsync()
    {
        var token = _cts.Token;
        var buffer = new byte[ushort.MaxValue];
        EndPoint anyEndPoint = new IPEndPoint(IPAddress.Any, 0);
        while (!token.IsCancellationRequested)
        {
            try
            {
                var result = await _local.ReceiveFromAsync(buffer, SocketFlags.None, anyEndPoint, token);
                var upstream = await GetSessionAsync(result.RemoteEndPoint, token);
                if (upstream is null)
                {
                    continue;
                }

                var packet = buffer.AsMemory(0, result.ReceivedBytes);
                if (AwgDecoys.IsHandshakeInitiation(packet.Span))
                {
                    foreach (var decoy in _decoys.BuildPrelude())
                    {
                        await upstream.SendAsync(decoy, SocketFlags.None, token);
                    }
                }
                await upstream.SendAsync(packet, SocketFlags.None, token);
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
            {
                break;
            }
            catch (SocketException)
            {
                // A lost datagram: WireGuard retransmits on its own.
            }
            catch (Exception ex)
            {
                Logging.SaveLog(_tag, ex);
            }
        }
    }

    private async Task ReceiveUpstreamAsync(Socket upstream, EndPoint client)
    {
        var token = _cts.Token;
        var buffer = new byte[ushort.MaxValue];
        while (!token.IsCancellationRequested)
        {
            try
            {
                var received = await upstream.ReceiveAsync(buffer, SocketFlags.None, token);
                await _local.SendToAsync(buffer.AsMemory(0, received), SocketFlags.None, client, token);
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
            {
                break;
            }
            catch (SocketException)
            {
                // Same as above: the next datagram decides.
            }
        }
    }

    /// <summary>One upstream socket per local sender, so a speed test and the main core never share one.</summary>
    private async Task<Socket?> GetSessionAsync(EndPoint client, CancellationToken token)
    {
        if (_sessions.TryGetValue(client, out var existing))
        {
            return existing;
        }

        var remote = await ResolveRemoteAsync(token);
        if (remote is null)
        {
            return null;
        }

        if (_sessions.Count >= MaxSessions)
        {
            foreach (var key in _sessions.Keys.ToList())
            {
                if (_sessions.TryRemove(key, out var stale))
                {
                    stale.Dispose();
                }
            }
        }

        var upstream = CreateUdpSocket(remote.AddressFamily);
        if (_bindAddress is not null && _bindAddress.AddressFamily == remote.AddressFamily)
        {
            try
            {
                upstream.Bind(new IPEndPoint(_bindAddress, 0));
            }
            catch (SocketException)
            {
                // The adapter changed since the config was written: the system route still works.
            }
        }
        await upstream.ConnectAsync(remote, token);

        _sessions[client] = upstream;
        _ = Task.Run(() => ReceiveUpstreamAsync(upstream, client));
        return upstream;
    }

    /// <summary>
    /// Resolved on the first packet, not when the config is written, so writing a config never
    /// waits on DNS. IPv4 comes first: SvoRay keeps only the server's IPv4 routes out of TUN.
    /// </summary>
    private async Task<IPEndPoint?> ResolveRemoteAsync(CancellationToken token)
    {
        if (_remote is not null)
        {
            return _remote;
        }

        if (IPAddress.TryParse(_host, out var literal))
        {
            return _remote = new IPEndPoint(literal, _port);
        }

        try
        {
            var addresses = await Dns.GetHostAddressesAsync(_host, token).WaitAsync(TimeSpan.FromSeconds(5), token);
            var address = addresses.FirstOrDefault(item => item.AddressFamily == AddressFamily.InterNetwork)
                          ?? addresses.FirstOrDefault();
            return address is null ? null : _remote = new IPEndPoint(address, _port);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Not resolvable yet; the next handshake attempt asks again.
            return null;
        }
    }

    private static Socket CreateUdpSocket(AddressFamily family)
    {
        var socket = new Socket(family, SocketType.Dgram, ProtocolType.Udp);
        if (OperatingSystem.IsWindows())
        {
            try
            {
                socket.IOControl(SioUdpConnReset, [0, 0, 0, 0], null);
            }
            catch
            {
                // Not fatal: such a receive error is caught and the loop goes on.
            }
        }
        return socket;
    }
}
