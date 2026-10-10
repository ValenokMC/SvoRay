using AwesomeAssertions;
using ServiceLib.Handler;
using Xunit;

namespace ServiceLib.Tests.Handler;

/// <summary>
/// The forwarding guard with netsh and the adapter state faked: it must change nothing on a PC
/// where forwarding is off, turn it off where something turned it on, do so again if that
/// software turns it back on, and always give the adapter back.
/// </summary>
[Collection("SvoRayForwarding")]
public class SvoRayForwardingTests : IDisposable
{
    private readonly Func<string, Task<int>> _run = SvoRayForwarding.RunNetsh;
    private readonly Func<int, bool?> _read = SvoRayForwarding.IsForwardingEnabled;
    private readonly Func<string, int?> _find = SvoRayForwarding.FindInterfaceIndex;
    private readonly List<string> _commands = [];
    private readonly Dictionary<int, bool> _forwarding = [];

    public SvoRayForwardingTests()
    {
        SvoRayForwarding.FindInterfaceIndex = name => name switch { "Ethernet" => 4, "Wi-Fi" => 7, _ => null };
        SvoRayForwarding.IsForwardingEnabled = index => _forwarding.TryGetValue(index, out var on) ? on : null;
        SvoRayForwarding.RunNetsh = arguments =>
        {
            _commands.Add(arguments);
            var index = int.Parse(arguments.Split("interface=")[1].Split(' ')[0]);
            _forwarding[index] = arguments.Contains("forwarding=enabled");
            return Task.FromResult(0);
        };
    }

    public void Dispose()
    {
        SvoRayForwarding.RunNetsh = _run;
        SvoRayForwarding.IsForwardingEnabled = _read;
        SvoRayForwarding.FindInterfaceIndex = _find;
    }

    private static Config NewConfig() => new() { SvoRayItem = new(), GuiItem = new() };

    [Fact]
    public void Command_ShouldOnlyTouchTheActiveStore()
    {
        SvoRayForwarding.Command(4, false).Should().Be("interface ipv4 set interface interface=4 forwarding=disabled store=active");
        SvoRayForwarding.Command(4, true).Should().Be("interface ipv4 set interface interface=4 forwarding=enabled store=active");
    }

    [Fact]
    public async Task SuspendAsync_ShouldChangeNothingWhereForwardingIsOff()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }
        _forwarding[4] = false;
        var config = NewConfig();

        (await SvoRayForwarding.SuspendAsync(config, "Ethernet")).Should().BeTrue();
        await SvoRayForwarding.RestoreAsync(config);

        _commands.Should().BeEmpty();
        config.SvoRayItem.SuspendedForwardingInterface.Should().BeNull();
    }

    [Fact]
    public async Task SuspendAsync_ShouldTurnForwardingOffAgainAndRestoreItOnce()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }
        _forwarding[4] = true;
        var config = NewConfig();

        await SvoRayForwarding.SuspendAsync(config, "Ethernet");
        _forwarding[4].Should().BeFalse();
        config.SvoRayItem.SuspendedForwardingInterface.Should().Be(4);

        // The sandbox turns it on again while TUN runs: the periodic check turns it off again.
        _forwarding[4] = true;
        await SvoRayForwarding.SuspendAsync(config, "Ethernet");
        _forwarding[4].Should().BeFalse();

        await SvoRayForwarding.RestoreAsync(config);
        await SvoRayForwarding.RestoreAsync(config);
        _forwarding[4].Should().BeTrue();
        config.SvoRayItem.SuspendedForwardingInterface.Should().BeNull();
        _commands.Count(c => c.Contains("forwarding=enabled")).Should().Be(1);
    }

    [Fact]
    public async Task SuspendAsync_ShouldGiveTheFirstAdapterBackWhenTheNetworkChanges()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }
        _forwarding[4] = true;
        _forwarding[7] = true;
        var config = NewConfig();

        await SvoRayForwarding.SuspendAsync(config, "Ethernet");
        await SvoRayForwarding.SuspendAsync(config, "Wi-Fi");

        _forwarding[4].Should().BeTrue("Ethernet is no longer the TUN's adapter");
        _forwarding[7].Should().BeFalse();
        config.SvoRayItem.SuspendedForwardingInterface.Should().Be(7);
    }

    [Fact]
    public async Task SuspendAsync_ShouldIgnoreAnUnknownOrMissingAdapter()
    {
        var config = NewConfig();

        (await SvoRayForwarding.SuspendAsync(config, null)).Should().BeTrue();
        (await SvoRayForwarding.SuspendAsync(config, "Bluetooth")).Should().BeTrue();

        _commands.Should().BeEmpty();
    }
}
