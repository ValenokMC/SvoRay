using AwesomeAssertions;
using ServiceLib.Handler;
using Xunit;

namespace ServiceLib.Tests.Handler;

/// <summary>
/// The kill switch's filters in the real Windows Filtering Platform. Runs on Windows with
/// administrator rights (the CI runner has them) and is skipped elsewhere.
/// </summary>
/// <remarks>
/// The full plan is added disabled - WFP checks every filter and stores it, but does not apply it -
/// so the runner keeps its network. What the filters do is checked with a few enforced filters that
/// touch only two Cloudflare addresses and one local port.
/// </remarks>
[Collection("SvoRayKillSwitch")]
public class SvoRayWfpWindowsTests
{
    private static readonly IPAddress Blocked = IPAddress.Parse("1.1.1.1");
    private static readonly IPAddress Allowed = IPAddress.Parse("1.0.0.1");

    private static void RequireWindowsAdministrator()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The Windows Filtering Platform exists on Windows only.");
        Assert.SkipUnless(Utils.IsAdministrator(), "Adding WFP filters needs administrator rights.");
    }

    [Fact]
    public void FullPlan_ShouldBeAcceptedByWindows()
    {
        RequireWindowsAdministrator();

        var adapter = NetworkInterface.GetAllNetworkInterfaces()
            .Select(n => SvoRayWfp.FindInterfaceLuid(n.Name))
            .FirstOrDefault(luid => luid is not null);
        adapter.Should().NotBeNull("every Windows PC has at least the loopback adapter");

        var plan = SvoRayKillSwitch.Plan([Environment.ProcessPath!], [adapter!.Value]);

        // Each filter alone first, so a refusal names the filter and WFP's error code.
        var refused = new List<string>();
        foreach (var filter in plan)
        {
            using var single = new SvoRayWfp(enforce: false);
            if (!single.Apply([filter]))
            {
                refused.Add($"{filter.Name} on {filter.Layer}: {single.LastError}");
            }
        }
        refused.Should().BeEmpty();

        using var firewall = new SvoRayWfp(enforce: false);
        firewall.Apply(plan).Should().BeTrue(firewall.LastError);
        firewall.FilterCount.Should().Be(plan.Count);

        // An update replaces every filter in one transaction.
        firewall.Apply(plan).Should().BeTrue();
        firewall.FilterCount.Should().Be(plan.Count);
    }

    [Fact]
    public async Task EnforcedFilters_ShouldBlockAndLetProgramsThrough()
    {
        RequireWindowsAdministrator();
        Assert.SkipUnless(await CanConnect(Blocked, 80) && await CanConnect(Allowed, 80), "No internet on this machine.");

        var filters = new List<KillSwitchFilter>
        {
            new("test block", KillSwitchLayer.ConnectV4, false, SvoRayKillSwitch.BlockWeight,
            [
                new KillSwitchCondition.RemoteNetwork(Blocked, 32),
                new KillSwitchCondition.Protocol(6),
                new KillSwitchCondition.RemotePort(80),
            ]),
            new("test block", KillSwitchLayer.ConnectV4, false, SvoRayKillSwitch.BlockWeight,
                [new KillSwitchCondition.RemoteNetwork(Allowed, 32)]),
            // Only to the second address, so the first one shows the block alone.
            new("test program", KillSwitchLayer.ConnectV4, true, SvoRayKillSwitch.PermitWeight,
            [
                new KillSwitchCondition.Application(Environment.ProcessPath!),
                new KillSwitchCondition.RemoteNetwork(Allowed, 32),
            ]),
        };

        using (var firewall = new SvoRayWfp())
        {
            firewall.Apply(filters).Should().BeTrue();

            (await CanConnect(Blocked, 80)).Should().BeFalse("a hard block on that address is in force");
            (await CanConnect(Allowed, 80)).Should().BeTrue("this program is let through to that address");
        }

        (await CanConnect(Blocked, 80)).Should().BeTrue("closing the session removes every filter");
    }

    [Fact]
    public async Task EnforcedFilters_ShouldLetLoopbackThrough()
    {
        RequireWindowsAdministrator();

        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = (ushort)((IPEndPoint)listener.LocalEndpoint).Port;
        var block = new KillSwitchFilter("test block", KillSwitchLayer.ConnectV4, false, SvoRayKillSwitch.BlockWeight,
        [
            new KillSwitchCondition.RemoteNetwork(IPAddress.Loopback, 32),
            new KillSwitchCondition.RemotePort(port),
        ]);
        var loopback = new KillSwitchFilter("test loopback", KillSwitchLayer.ConnectV4, true, SvoRayKillSwitch.PermitWeight,
            [new KillSwitchCondition.Loopback()]);

        using var firewall = new SvoRayWfp();

        firewall.Apply([block]).Should().BeTrue();
        (await CanConnect(IPAddress.Loopback, port)).Should().BeFalse("the block covers that local port");

        firewall.Apply([block, loopback]).Should().BeTrue();
        (await CanConnect(IPAddress.Loopback, port)).Should().BeTrue("loopback is let through above the block");
    }

    private static async Task<bool> CanConnect(IPAddress address, int port)
    {
        using var client = new TcpClient(address.AddressFamily);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            await client.ConnectAsync(address, port, timeout.Token);
            return true;
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException)
        {
            return false;
        }
    }
}
