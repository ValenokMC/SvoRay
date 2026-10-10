namespace ServiceLib.Handler;

/// <summary>
/// Keeps IPv4 forwarding off on the physical adapter while TUN runs, and puts it back afterwards.
/// </summary>
/// <remarks>
/// The core sends "direct" traffic past its own TUN by binding each socket to the physical
/// adapter (IP_UNICAST_IF). Windows honours that binding only while the adapter is not forwarding.
/// Software that shares the connection with a virtual network - a sandbox gateway, Internet
/// Connection Sharing - turns forwarding on, and Windows then routes even bound packets by the
/// TUN's more specific routes: they stall on the TUN and are lost. Every direct destination
/// (Steam, Russian sites, games in game mode, the core's own DNS) times out, while traffic through
/// the VPN server works.
/// <para>
/// Measured on a PC with the Codex sandbox's "vgate0": with forwarding on, a bound DNS query,
/// a TCP connect to Yandex and one to a Steam CM server all timed out; with forwarding off they
/// took 8, 22 and 53 ms, and unbound traffic still went into the TUN.
/// </para>
/// <para>
/// The change is made in the active store only, so a reboot undoes it whatever happens, and the
/// adapter is recorded in the config so a crash is undone at the next start.
/// </para>
/// </remarks>
public static class SvoRayForwarding
{
    private const string _tag = "SvoRay forwarding";

    /// <summary>Runs <c>netsh</c> with the given arguments and returns its exit code.</summary>
    public static Func<string, Task<int>> RunNetsh { get; set; } = RunNetshProcess;

    /// <summary>Whether IPv4 forwarding is on for the adapter with that interface index.</summary>
    public static Func<int, bool?> IsForwardingEnabled { get; set; } = ReadForwarding;

    /// <summary>The IPv4 interface index of the adapter called <paramref name="name"/>, if any.</summary>
    public static Func<string, int?> FindInterfaceIndex { get; set; } = ReadInterfaceIndex;

    public static string Command(int interfaceIndex, bool enable)
    {
        return $"interface ipv4 set interface interface={interfaceIndex} forwarding={(enable ? "enabled" : "disabled")} store=active";
    }

    /// <summary>
    /// Turns forwarding off on <paramref name="interfaceName"/> if something turned it on, and
    /// remembers to turn it back on. Safe to call repeatedly: a later call re-applies the change
    /// if other software switched forwarding back on in the meantime.
    /// </summary>
    /// <returns>True when forwarding is off afterwards (or was never on).</returns>
    public static async Task<bool> SuspendAsync(Config config, string? interfaceName)
    {
        if (!OperatingSystem.IsWindows() || interfaceName.IsNullOrEmpty())
        {
            return true;
        }

        var index = FindInterfaceIndex(interfaceName!);
        if (index is null || IsForwardingEnabled(index.Value) != true)
        {
            return true;
        }

        // Another adapter recorded earlier (the user changed networks): give it back first.
        if (config.SvoRayItem.SuspendedForwardingInterface is { } previous && previous != index.Value)
        {
            await RestoreAsync(config);
        }

        var code = await RunNetsh(Command(index.Value, false));
        if (code != 0)
        {
            Logging.SaveLog($"{_tag}: could not turn forwarding off on interface {index.Value} (netsh {code})");
            return false;
        }

        if (config.SvoRayItem.SuspendedForwardingInterface != index.Value)
        {
            config.SvoRayItem.SuspendedForwardingInterface = index.Value;
            await ConfigHandler.SaveConfig(config);
            Logging.SaveLog($"{_tag}: forwarding turned off on interface {index.Value} for TUN");
        }
        else
        {
            Logging.SaveLog($"{_tag}: forwarding was turned on again on interface {index.Value}; turned off");
        }
        return true;
    }

    /// <summary>Turns forwarding back on where <see cref="SuspendAsync"/> turned it off.</summary>
    public static async Task RestoreAsync(Config config)
    {
        if (!OperatingSystem.IsWindows() || config.SvoRayItem.SuspendedForwardingInterface is not { } index)
        {
            return;
        }

        var code = await RunNetsh(Command(index, true));
        // An adapter that is gone cannot be restored, and a reboot has reset it anyway.
        Logging.SaveLog(code == 0
            ? $"{_tag}: forwarding turned back on on interface {index}"
            : $"{_tag}: could not turn forwarding back on on interface {index} (netsh {code})");
        config.SvoRayItem.SuspendedForwardingInterface = null;
        await ConfigHandler.SaveConfig(config);
    }

    private static async Task<int> RunNetshProcess(string arguments)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.SystemDirectory, "netsh.exe"),
                Arguments = arguments,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });
            if (process is null)
            {
                return -1;
            }
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await process.WaitForExitAsync(cts.Token);
            return process.ExitCode;
        }
        catch (Exception ex)
        {
            Logging.SaveLog($"{_tag}: netsh failed: {ex.GetType().Name}");
            return -1;
        }
    }

    /// <summary>
    /// The adapter's own forwarding flag. .NET's <c>IPv4InterfaceProperties.IsForwardingEnabled</c>
    /// cannot be used: on Windows it reports the global routing switch, which is off even while
    /// an adapter forwards, so it said "off" on exactly the PCs this guard exists for.
    /// </summary>
    private static bool? ReadForwarding(int interfaceIndex)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        // MIB_IPINTERFACE_ROW: Family at 0, the 8-byte InterfaceLuid at 8, InterfaceIndex at 16,
        // ForwardingEnabled at 41. 512 bytes is well over the structure's size.
        var row = Marshal.AllocHGlobal(512);
        try
        {
            for (var i = 0; i < 512; i++)
            {
                Marshal.WriteByte(row, i, 0);
            }
            Marshal.WriteInt16(row, 0, 2);
            Marshal.WriteInt32(row, 16, interfaceIndex);
            return GetIpInterfaceEntry(row) == 0 ? Marshal.ReadByte(row, 41) != 0 : null;
        }
        finally
        {
            Marshal.FreeHGlobal(row);
        }
    }

    [DllImport("iphlpapi.dll")]
    private static extern int GetIpInterfaceEntry(IntPtr row);

    private static int? ReadInterfaceIndex(string name)
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .FirstOrDefault(n => string.Equals(n.Name, name, StringComparison.OrdinalIgnoreCase))
                ?.GetIPProperties().GetIPv4Properties()?.Index;
        }
        catch (Exception ex) when (ex is NetworkInformationException or PlatformNotSupportedException)
        {
            return null;
        }
    }
}
