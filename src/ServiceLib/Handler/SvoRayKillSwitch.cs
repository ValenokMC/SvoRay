namespace ServiceLib.Handler;

/// <summary>Where a kill switch filter applies: outgoing connects or accepted incoming ones, per IP version.</summary>
public enum KillSwitchLayer
{
    ConnectV4,
    ConnectV6,
    AcceptV4,
    AcceptV6,
}

/// <summary>
/// One condition of a kill switch filter. Conditions on different fields must all match;
/// a filter without conditions matches everything on its layer.
/// </summary>
public abstract record KillSwitchCondition
{
    /// <summary>Traffic that never leaves the PC (127.0.0.1, ::1, the local proxy port).</summary>
    public sealed record Loopback : KillSwitchCondition;

    /// <summary>Traffic of the program at <paramref name="Path"/>.</summary>
    public sealed record Application(string Path) : KillSwitchCondition;

    /// <summary>Traffic through the network adapter with that LUID.</summary>
    public sealed record Interface(ulong Luid) : KillSwitchCondition;

    public sealed record Protocol(byte Number) : KillSwitchCondition;

    public sealed record LocalPort(ushort Port) : KillSwitchCondition;

    public sealed record RemotePort(ushort Port) : KillSwitchCondition;

    public sealed record RemoteNetwork(IPAddress Address, byte PrefixLength) : KillSwitchCondition;
}

public sealed record KillSwitchFilter(
    string Name,
    KillSwitchLayer Layer,
    bool Permit,
    byte Weight,
    IReadOnlyList<KillSwitchCondition> Conditions);

/// <summary>The firewall the kill switch writes its filters into.</summary>
public interface IKillSwitchFirewall : IDisposable
{
    /// <summary>
    /// Replaces the filters this firewall added before with <paramref name="filters"/>, all at
    /// once. On failure the previous filters stay in force.
    /// </summary>
    bool Apply(IReadOnlyList<KillSwitchFilter> filters);
}

/// <summary>
/// Kill switch: while the user wants the VPN on, nothing reaches the internet except through it.
/// </summary>
/// <remarks>
/// Built on the Windows Filtering Platform, like the kill switches of WireGuard and Mullvad. A
/// block-all filter sits under a few exceptions: the tunnel adapter, the cores and SvoRay itself
/// (they carry the VPN and the "direct" routes: game mode, the domain list, the server's rules),
/// loopback (the local proxy port), DHCP and the local network. DNS to the local network is not an
/// exception - a router forwards it to the provider, which would show every site the PC opens.
/// <para>
/// The filters live in a dynamic WFP session: Windows deletes them when SvoRay closes or crashes,
/// or when an update kills it. A kill switch that outlived its program would leave the PC without
/// internet and with nothing on screen to explain why, so this one never does.
/// </para>
/// </remarks>
public static class SvoRayKillSwitch
{
    private const string _tag = "SvoRay kill switch";

    public const byte PermitWeight = 15;
    public const byte DnsBlockWeight = 13;
    public const byte LanWeight = 12;
    public const byte BlockWeight = 0;

    private const byte Tcp = 6;
    private const byte Udp = 17;

    /// <summary>The adapters sing-box and Xray create for TUN on Windows.</summary>
    public static readonly string[] TunnelAdapterNames = ["singbox_tun", "xray_tun"];

    /// <summary>Private, link-local and multicast ranges: printers, the router's page, a NAS.</summary>
    public static readonly (IPAddress Address, byte Prefix)[] LocalNetworksV4 =
    [
        (IPAddress.Parse("10.0.0.0"), 8),
        (IPAddress.Parse("172.16.0.0"), 12),
        (IPAddress.Parse("192.168.0.0"), 16),
        (IPAddress.Parse("169.254.0.0"), 16),
        (IPAddress.Parse("224.0.0.0"), 4),
        (IPAddress.Parse("255.255.255.255"), 32),
    ];

    public static readonly (IPAddress Address, byte Prefix)[] LocalNetworksV6 =
    [
        (IPAddress.Parse("fe80::"), 10),
        (IPAddress.Parse("fc00::"), 7),
        (IPAddress.Parse("ff00::"), 8),
    ];

    public static Func<IKillSwitchFirewall> CreateFirewall { get; set; } = CreateDefaultFirewall;

    /// <summary>The LUID of the adapter with that name, or null when there is none.</summary>
    public static Func<string, ulong?> FindInterfaceLuid { get; set; } = SvoRayWfp.FindInterfaceLuid;

    /// <summary>Programs whose traffic is the VPN itself.</summary>
    public static Func<IReadOnlyList<string>> FindTrustedApps { get; set; } = DefaultTrustedApps;

    private static readonly Lock _lock = new();
    private static IKillSwitchFirewall? _firewall;
    private static string? _appliedKey;

    public static bool IsEngaged
    {
        get
        {
            lock (_lock)
            {
                return _firewall is not null;
            }
        }
    }

    /// <summary>
    /// The filters for the given trusted programs and tunnel adapters, the same on all four layers
    /// apart from DHCP's ports and DNS, which only outgoing traffic needs.
    /// </summary>
    public static IReadOnlyList<KillSwitchFilter> Plan(IEnumerable<string> trustedApps, IEnumerable<ulong> tunnelInterfaces)
    {
        var apps = trustedApps.ToList();
        var tunnels = tunnelInterfaces.ToList();
        var filters = new List<KillSwitchFilter>();

        foreach (var layer in Enum.GetValues<KillSwitchLayer>())
        {
            var v6 = layer is KillSwitchLayer.ConnectV6 or KillSwitchLayer.AcceptV6;
            var outgoing = layer is KillSwitchLayer.ConnectV4 or KillSwitchLayer.ConnectV6;

            filters.Add(Permit("Loopback", layer, PermitWeight, new KillSwitchCondition.Loopback()));
            foreach (var luid in tunnels)
            {
                filters.Add(Permit("VPN tunnel", layer, PermitWeight, new KillSwitchCondition.Interface(luid)));
            }
            foreach (var app in apps)
            {
                filters.Add(Permit("VPN program", layer, PermitWeight, new KillSwitchCondition.Application(app)));
            }

            // The address lease: without it the PC drops off its own network a few hours in.
            filters.Add(Permit("DHCP", layer, PermitWeight,
                new KillSwitchCondition.Protocol(Udp),
                new KillSwitchCondition.LocalPort((ushort)(v6 ? 546 : 68)),
                new KillSwitchCondition.RemotePort((ushort)(v6 ? 547 : 67))));

            if (outgoing)
            {
                filters.Add(Block("DNS outside the VPN", layer, DnsBlockWeight,
                    new KillSwitchCondition.Protocol(Udp), new KillSwitchCondition.RemotePort(53)));
                filters.Add(Block("DNS outside the VPN", layer, DnsBlockWeight,
                    new KillSwitchCondition.Protocol(Tcp), new KillSwitchCondition.RemotePort(53)));
            }

            foreach (var (address, prefix) in v6 ? LocalNetworksV6 : LocalNetworksV4)
            {
                filters.Add(Permit("Local network", layer, LanWeight,
                    new KillSwitchCondition.RemoteNetwork(address, prefix)));
            }

            filters.Add(Block("Everything else", layer, BlockWeight));
        }

        return filters;
    }

    /// <summary>
    /// Puts the kill switch up, or brings it up to date: the tunnel adapter appears only once the
    /// core is running and gets a new LUID when the core restarts, so this is called again on
    /// every state change and on a timer. Cheap when nothing changed.
    /// </summary>
    /// <returns>False when the filters could not be set.</returns>
    public static bool Engage()
    {
        lock (_lock)
        {
            try
            {
                var apps = FindTrustedApps();
                var tunnels = TunnelAdapterNames
                    .Select(FindInterfaceLuid)
                    .OfType<ulong>()
                    .Distinct()
                    .Order()
                    .ToList();
                var key = string.Join('|', apps) + "#" + string.Join(',', tunnels);
                if (_firewall is not null && key == _appliedKey)
                {
                    return true;
                }

                var fresh = _firewall is null;
                _firewall ??= CreateFirewall();
                if (!_firewall.Apply(Plan(apps, tunnels)))
                {
                    if (fresh)
                    {
                        _firewall.Dispose();
                        _firewall = null;
                    }
                    Logging.SaveLog($"{_tag}: could not apply the filters");
                    return false;
                }

                Logging.SaveLog($"{_tag}: {(fresh ? "on" : "updated")}, {apps.Count} programs, {tunnels.Count} tunnel adapters");
                _appliedKey = key;
                return true;
            }
            catch (Exception ex)
            {
                Logging.SaveLog(_tag, ex);
                return false;
            }
        }
    }

    /// <summary>Takes every filter down; the internet works as without SvoRay.</summary>
    public static void Disengage()
    {
        lock (_lock)
        {
            if (_firewall is null)
            {
                return;
            }
            try
            {
                _firewall.Dispose();
                Logging.SaveLog($"{_tag}: off");
            }
            catch (Exception ex)
            {
                Logging.SaveLog(_tag, ex);
            }
            finally
            {
                _firewall = null;
                _appliedKey = null;
            }
        }
    }

    private static KillSwitchFilter Permit(string name, KillSwitchLayer layer, byte weight, params KillSwitchCondition[] conditions)
        => new(name, layer, true, weight, conditions);

    private static KillSwitchFilter Block(string name, KillSwitchLayer layer, byte weight, params KillSwitchCondition[] conditions)
        => new(name, layer, false, weight, conditions);

    private static IKillSwitchFirewall CreateDefaultFirewall()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("The kill switch needs the Windows Filtering Platform.");
        }
        return new SvoRayWfp();
    }

    /// <summary>
    /// SvoRay itself (its AmneziaWG relay talks to the server, and it updates subscriptions) and
    /// every program in its bin folder, which is where the cores are started from.
    /// </summary>
    private static IReadOnlyList<string> DefaultTrustedApps()
    {
        var apps = new List<string>();
        var exe = Utils.GetExePath();
        if (exe.IsNotEmpty() && File.Exists(exe))
        {
            apps.Add(exe);
        }

        try
        {
            var bin = Utils.GetBinPath("");
            if (Directory.Exists(bin))
            {
                apps.AddRange(Directory.EnumerateFiles(bin, "*.exe", SearchOption.AllDirectories).Take(64));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Logging.SaveLog($"{_tag}: could not list the cores: {ex.GetType().Name}");
        }

        return apps
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
