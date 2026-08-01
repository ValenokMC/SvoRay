using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace v2rayN.Manager;

internal static class SvoRayService
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwaSystemBackdropType = 38;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int valueSize);

    [DllImport("iphlpapi.dll", CharSet = CharSet.Auto)]
    private static extern int GetBestInterface(uint destinationAddress, out uint bestInterfaceIndex);

    public static void ApplyGlassBackdrop(Window window)
    {
        try
        {
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero)
            {
                return;
            }

            var enabled = 1;
            var rounded = 2;
            var mica = 2;
            _ = DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref enabled, sizeof(int));
            _ = DwmSetWindowAttribute(handle, DwmwaWindowCornerPreference, ref rounded, sizeof(int));
            _ = DwmSetWindowAttribute(handle, DwmwaSystemBackdropType, ref mica, sizeof(int));
        }
        catch
        {
            // The gradient background remains the fallback on older Windows builds.
        }
    }

    public static async Task PrepareTunAsync(Config config)
    {
        var network = GetPreferredNetwork();
        if (network is not null)
        {
            config.CoreBasicItem.BindInterface = network.Value.InterfaceName;
            config.CoreBasicItem.SendThrough = network.Value.LocalAddress;
        }

        config.TunModeItem.AutoRoute = true;
        config.TunModeItem.StrictRoute = true;
        config.TunModeItem.EnableLegacyProtect = false;
        config.TunModeItem.EnableIPv6Address = false;
        config.TunModeItem.Stack = "gvisor";
        config.TunModeItem.Mtu = 1500;

        config.SystemProxyItem.SysProxyType = ESysProxyType.ForcedClear;
        config.SimpleDNSItem.DirectDNS = "1.1.1.1";
        config.SimpleDNSItem.RemoteDNS = "https://1.1.1.1/dns-query";
        config.SimpleDNSItem.BootstrapDNS = "1.1.1.1";

        var routingItems = await AppManager.Instance.RoutingItems();
        var globalRouting = routingItems.FirstOrDefault(item =>
            item.Remarks?.Contains("(Global)", StringComparison.OrdinalIgnoreCase) == true);
        if (globalRouting is not null)
        {
            await ConfigHandler.SetDefaultRouting(config, globalRouting);
        }

        var profile = await ConfigHandler.GetDefaultServer(config);
        if (profile is not null)
        {
            config.TunModeItem.RouteExcludeAddress = await ResolveEndpointRoutes(profile.Address);
        }

        await ConfigHandler.SaveConfig(config);
    }

    private static async Task<List<string>> ResolveEndpointRoutes(string? address)
    {
        if (address.IsNullOrEmpty())
        {
            return [];
        }

        if (IPAddress.TryParse(address, out var literal))
        {
            return literal.AddressFamily == AddressFamily.InterNetwork
                ? [$"{literal}/32"]
                : [];
        }

        try
        {
            var addresses = await Dns.GetHostAddressesAsync(address!)
                .WaitAsync(TimeSpan.FromSeconds(5));
            return addresses
                .Where(item => item.AddressFamily == AddressFamily.InterNetwork)
                .Select(item => $"{item}/32")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    private static (string InterfaceName, string LocalAddress)? GetPreferredNetwork()
    {
        var interfaces = NetworkInterface.GetAllNetworkInterfaces()
            .Where(item => item.OperationalStatus == OperationalStatus.Up)
            .Where(item => item.NetworkInterfaceType is not NetworkInterfaceType.Loopback
                and not NetworkInterfaceType.Tunnel)
            .ToList();

        NetworkInterface? preferred = null;
        try
        {
            var destinationBytes = IPAddress.Parse("1.1.1.1").GetAddressBytes();
            var destination = BitConverter.ToUInt32(destinationBytes, 0);
            if (GetBestInterface(destination, out var index) == 0)
            {
                preferred = interfaces.FirstOrDefault(item =>
                    item.GetIPProperties().GetIPv4Properties()?.Index == index);
            }
        }
        catch
        {
            // Fall back to the first active adapter with an IPv4 gateway.
        }

        preferred ??= interfaces.FirstOrDefault(item =>
            item.GetIPProperties().GatewayAddresses.Any(gateway =>
                gateway.Address.AddressFamily == AddressFamily.InterNetwork));

        if (preferred is null)
        {
            return null;
        }

        var localAddress = preferred.GetIPProperties().UnicastAddresses
            .Select(item => item.Address)
            .FirstOrDefault(item => item.AddressFamily == AddressFamily.InterNetwork
                && !IPAddress.IsLoopback(item));

        return localAddress is null ? null : (preferred.Name, localAddress.ToString());
    }
}
