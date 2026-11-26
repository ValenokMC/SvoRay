using ServiceLib.Handler;

namespace ServiceLib.Tests.Handler;

// Inside the namespace: this scope is searched first, so Should() binds to AwesomeAssertions, not to TUnit's global Should().
using AwesomeAssertions;

// The fakes read fields the tests set; TUnit creates a new instance for every test, as xUnit did.
#pragma warning disable TUnit0018

/// <summary>
/// The kill switch's filter plan, and how it is put up and kept current, with the firewall and
/// the adapters faked. What WFP itself makes of the filters is checked on Windows in
/// <see cref="SvoRayWfpWindowsTests"/>.
/// </summary>
[NotInParallel("SvoRayKillSwitch")]
public class SvoRayKillSwitchTests : IDisposable
{
    private const string Core = @"C:\Program Files\SvoRay\bin\xray\xray.exe";
    private const string App = @"C:\Program Files\SvoRay\SvoRay.exe";

    private readonly Func<IKillSwitchFirewall> _create = SvoRayKillSwitch.CreateFirewall;
    private readonly Func<string, ulong?> _find = SvoRayKillSwitch.FindInterfaceLuid;
    private readonly Func<IReadOnlyList<string>> _apps = SvoRayKillSwitch.FindTrustedApps;
    private readonly Func<IReadOnlyList<(IPAddress Address, byte Prefix)>> _servers = SvoRayKillSwitch.FindServerNetworks;
    private readonly List<(IPAddress Address, byte Prefix)> _serverNetworks = [];
    private readonly List<FakeFirewall> _firewalls = [];
    private readonly Dictionary<string, ulong> _adapters = [];
    private bool _refuse;

    public SvoRayKillSwitchTests()
    {
        SvoRayKillSwitch.Disengage();
        SvoRayKillSwitch.CreateFirewall = () =>
        {
            var firewall = new FakeFirewall(this);
            _firewalls.Add(firewall);
            return firewall;
        };
        SvoRayKillSwitch.FindInterfaceLuid = name => _adapters.TryGetValue(name, out var luid) ? luid : null;
        SvoRayKillSwitch.FindTrustedApps = () => [App, Core];
        SvoRayKillSwitch.FindServerNetworks = () => _serverNetworks.ToList();
    }

    public void Dispose()
    {
        SvoRayKillSwitch.Disengage();
        SvoRayKillSwitch.CreateFirewall = _create;
        SvoRayKillSwitch.FindInterfaceLuid = _find;
        SvoRayKillSwitch.FindTrustedApps = _apps;
        SvoRayKillSwitch.FindServerNetworks = _servers;
    }

    [Test]
    public void Plan_ShouldLetAnyProgramReachTheVpnServer()
    {
        // TUN keeps the server's address out of the tunnel. A program that connects to it -
        // a provider can answer a blocked service's name with the server's address - goes out
        // directly, and the kill switch must not cut it off.
        var server4 = IPAddress.Parse("45.0.2.10");
        var server6 = IPAddress.Parse("2001:db8::10");
        var plan = SvoRayKillSwitch.Plan([Core], [42], [(server4, 32), (server6, 128)]);

        foreach (var layer in Enum.GetValues<KillSwitchLayer>())
        {
            var v6 = layer is KillSwitchLayer.ConnectV6 or KillSwitchLayer.AcceptV6;
            var server = v6
                ? new KillSwitchCondition.RemoteNetwork(server6, 128)
                : new KillSwitchCondition.RemoteNetwork(server4, 32);
            plan.Should().ContainSingle(f => f.Layer == layer && f.Name == "VPN server")
                .Which.Should().Match<KillSwitchFilter>(f =>
                    f.Permit && f.Weight == SvoRayKillSwitch.PermitWeight
                    && f.Conditions.Count == 1 && f.Conditions[0] == server);
        }
    }

    [Test]
    [Arguments("45.0.2.10/32", "45.0.2.10", 32)]
    [Arguments("45.0.2.10", "45.0.2.10", 32)]
    [Arguments(" 2001:db8::10/128 ", "2001:db8::10", 128)]
    public void ParseServerNetworks_ShouldTakeSingleHosts(string route, string address, byte prefix)
    {
        SvoRayKillSwitch.ParseServerNetworks([route])
            .Should().Equal((IPAddress.Parse(address), prefix));
    }

    [Test]
    [Arguments("10.0.0.0/8")]
    [Arguments("0.0.0.0/0")]
    [Arguments("2001:db8::/32")]
    [Arguments("vpn.example.com")]
    [Arguments("45.0.2.10/abc")]
    public void ParseServerNetworks_ShouldNotOpenWholeRanges(string route)
    {
        // A range typed into the advanced settings is not the server; opening it would be a hole.
        SvoRayKillSwitch.ParseServerNetworks([route]).Should().BeEmpty();
    }

    [Test]
    public void Engage_ShouldApplyAgainWhenTheServerChanges()
    {
        SvoRayKillSwitch.Engage().Should().BeTrue();
        _serverNetworks.Add((IPAddress.Parse("45.0.2.10"), 32));
        SvoRayKillSwitch.Engage().Should().BeTrue();

        var applied = _firewalls.Single().Applied;
        applied.Should().HaveCount(2);
        applied[1].Should().Contain(f => f.Name == "VPN server");
    }

    [Test]
    public void Plan_ShouldEndEveryLayerWithAHardBlockOfEverything()
    {
        var plan = SvoRayKillSwitch.Plan([Core], [42]);

        foreach (var layer in Enum.GetValues<KillSwitchLayer>())
        {
            plan.Should().ContainSingle(f => f.Layer == layer && !f.Permit && f.Conditions.Count == 0)
                .Which.Weight.Should().Be(SvoRayKillSwitch.BlockWeight);
            plan.Where(f => f.Layer == layer).Max(f => f.Weight).Should().Be(SvoRayKillSwitch.PermitWeight);
        }
    }

    [Test]
    public void Plan_ShouldLetTheTunnelTheCoresAndLoopbackThroughOnEveryLayer()
    {
        var plan = SvoRayKillSwitch.Plan([App, Core], [42, 43]);

        foreach (var layer in Enum.GetValues<KillSwitchLayer>())
        {
            var permits = plan.Where(f => f.Layer == layer && f.Permit && f.Weight == SvoRayKillSwitch.PermitWeight)
                .Where(f => f.Conditions.Count == 1)
                .Select(f => f.Conditions[0])
                .ToList();
            permits.Should().Contain(new KillSwitchCondition.Loopback());
            permits.Should().Contain(new KillSwitchCondition.Interface(42));
            permits.Should().Contain(new KillSwitchCondition.Interface(43));
            permits.Should().Contain(new KillSwitchCondition.Application(App));
            permits.Should().Contain(new KillSwitchCondition.Application(Core));
        }
    }

    [Test]
    public void Plan_WithoutATunnel_ShouldStillBlock()
    {
        var plan = SvoRayKillSwitch.Plan([Core], []);

        plan.Should().NotContain(f => f.Conditions.Any(c => c is KillSwitchCondition.Interface));
        plan.Count(f => !f.Permit && f.Conditions.Count == 0).Should().Be(4);
    }

    [Test]
    public void Plan_ShouldBlockDnsOutsideTheVpnAboveTheLocalNetwork()
    {
        var plan = SvoRayKillSwitch.Plan([Core], [42]);

        var dns = plan.Where(f => f.Name.StartsWith("DNS", StringComparison.Ordinal)).ToList();
        dns.Should().HaveCount(4);
        dns.Should().OnlyContain(f => !f.Permit
            && f.Weight > SvoRayKillSwitch.LanWeight
            && f.Weight < SvoRayKillSwitch.PermitWeight
            && (f.Layer == KillSwitchLayer.ConnectV4 || f.Layer == KillSwitchLayer.ConnectV6)
            && f.Conditions.Contains(new KillSwitchCondition.RemotePort(53)));
        var protocols = dns.Select(f => f.Conditions.OfType<KillSwitchCondition.Protocol>().Single().Number).ToList();
        protocols.Count(p => p == 6).Should().Be(2, "TCP on both connect layers");
        protocols.Count(p => p == 17).Should().Be(2, "UDP on both connect layers");
    }

    [Test]
    public void Plan_ShouldOnlyOpenPrivateRangesForTheLocalNetwork()
    {
        var plan = SvoRayKillSwitch.Plan([Core], [42]);

        var lan = plan.Where(f => f.Weight == SvoRayKillSwitch.LanWeight).ToList();
        lan.Should().OnlyContain(f => f.Permit);
        var networks = lan.Select(f => (KillSwitchCondition.RemoteNetwork)f.Conditions.Single()).ToList();

        foreach (var network in networks)
        {
            var v6Layer = lan.First(f => f.Conditions.Single() == network).Layer
                is KillSwitchLayer.ConnectV6 or KillSwitchLayer.AcceptV6;
            (network.Address.AddressFamily == AddressFamily.InterNetworkV6).Should().Be(v6Layer);
        }

        string[] publicAddresses = ["1.1.1.1", "8.8.8.8", "77.88.8.8", "100.64.0.1", "2606:4700:4700::1111"];
        foreach (var address in publicAddresses.Select(IPAddress.Parse))
        {
            networks.Should().NotContain(n => Contains(n, address), $"{address} is on the internet");
        }

        string[] localAddresses = ["192.168.1.1", "10.0.0.138", "172.20.1.2", "169.254.10.1", "239.255.255.250", "fe80::1", "fd00::1"];
        foreach (var address in localAddresses.Select(IPAddress.Parse))
        {
            networks.Should().Contain(n => Contains(n, address), $"{address} is on the local network");
        }
    }

    [Test]
    public void Plan_ShouldKeepTheAddressLease()
    {
        var plan = SvoRayKillSwitch.Plan([], []);

        var dhcp = plan.Where(f => f.Name == "DHCP").ToList();
        dhcp.Should().HaveCount(4).And.OnlyContain(f => f.Permit);
        var v4 = dhcp.Single(f => f.Layer == KillSwitchLayer.ConnectV4).Conditions;
        v4.Should().HaveCount(3);
        v4.Should().Contain(new KillSwitchCondition.Protocol(17));
        v4.Should().Contain(new KillSwitchCondition.LocalPort(68));
        v4.Should().Contain(new KillSwitchCondition.RemotePort(67));
        dhcp.Single(f => f.Layer == KillSwitchLayer.AcceptV6).Conditions.Should()
            .Contain(new KillSwitchCondition.LocalPort(546)).And.Contain(new KillSwitchCondition.RemotePort(547));
    }

    [Test]
    public void Engage_ShouldApplyOnceAndAgainOnlyWhenTheTunnelChanges()
    {
        SvoRayKillSwitch.Engage().Should().BeTrue();
        SvoRayKillSwitch.IsEngaged.Should().BeTrue();
        SvoRayKillSwitch.Engage().Should().BeTrue();
        _firewalls.Should().ContainSingle().Which.Applied.Should().HaveCount(1);

        _adapters["singbox_tun"] = 77;
        SvoRayKillSwitch.Engage().Should().BeTrue();

        var firewall = _firewalls.Single();
        firewall.Applied.Should().HaveCount(2);
        firewall.Applied[0].Should().NotContain(f => f.Conditions.Contains(new KillSwitchCondition.Interface(77)));
        firewall.Applied[1].Should().Contain(f => f.Conditions.Contains(new KillSwitchCondition.Interface(77)));
    }

    [Test]
    public void Engage_WhenWindowsRefuses_ShouldReportItAndHoldNothing()
    {
        _refuse = true;

        SvoRayKillSwitch.Engage().Should().BeFalse();

        SvoRayKillSwitch.IsEngaged.Should().BeFalse();
        _firewalls.Single().Disposed.Should().BeTrue();
    }

    [Test]
    public void Engage_WhenAnUpdateIsRefused_ShouldKeepTheFiltersInForce()
    {
        SvoRayKillSwitch.Engage().Should().BeTrue();
        _refuse = true;
        _adapters["xray_tun"] = 5;

        SvoRayKillSwitch.Engage().Should().BeFalse();

        SvoRayKillSwitch.IsEngaged.Should().BeTrue();
        _firewalls.Single().Disposed.Should().BeFalse();

        // The next attempt tries the update again.
        _refuse = false;
        SvoRayKillSwitch.Engage().Should().BeTrue();
        _firewalls.Single().Applied.Last().Should().Contain(f => f.Conditions.Contains(new KillSwitchCondition.Interface(5)));
    }

    [Test]
    public void Disengage_ShouldCloseTheFirewall()
    {
        SvoRayKillSwitch.Engage();

        SvoRayKillSwitch.Disengage();

        SvoRayKillSwitch.IsEngaged.Should().BeFalse();
        _firewalls.Single().Disposed.Should().BeTrue();
        SvoRayKillSwitch.Disengage();
    }

    [Test]
    [Arguments("192.168.0.0", 16, 0xC0A80000u, 0xFFFF0000u)]
    [Arguments("255.255.255.255", 32, 0xFFFFFFFFu, 0xFFFFFFFFu)]
    [Arguments("224.0.0.0", 4, 0xE0000000u, 0xF0000000u)]
    [Arguments("10.0.0.0", 8, 0x0A000000u, 0xFF000000u)]
    [Arguments("0.0.0.0", 0, 0u, 0u)]
    public void V4AddressAndMask_ShouldNotOverflowOnHighAddresses(string address, byte prefix, uint expectedAddress, uint expectedMask)
    {
        var (value, mask) = SvoRayWfp.V4AddressAndMask(IPAddress.Parse(address), prefix);

        value.Should().Be(expectedAddress);
        mask.Should().Be(expectedMask);
    }

    private static bool Contains(KillSwitchCondition.RemoteNetwork network, IPAddress address)
    {
        if (network.Address.AddressFamily != address.AddressFamily)
        {
            return false;
        }
        var a = network.Address.GetAddressBytes();
        var b = address.GetAddressBytes();
        for (var bit = 0; bit < network.PrefixLength; bit++)
        {
            var mask = 0x80 >> (bit % 8);
            if ((a[bit / 8] & mask) != (b[bit / 8] & mask))
            {
                return false;
            }
        }
        return true;
    }

    private sealed class FakeFirewall(SvoRayKillSwitchTests owner) : IKillSwitchFirewall
    {
        public List<IReadOnlyList<KillSwitchFilter>> Applied { get; } = [];
        public bool Disposed { get; private set; }

        public bool Apply(IReadOnlyList<KillSwitchFilter> filters)
        {
            if (owner._refuse)
            {
                return false;
            }
            Applied.Add(filters);
            return true;
        }

        public void Dispose() => Disposed = true;
    }
}
