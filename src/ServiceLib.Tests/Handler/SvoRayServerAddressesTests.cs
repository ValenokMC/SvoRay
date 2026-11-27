using ServiceLib.Handler;

namespace ServiceLib.Tests.Handler;

// Inside the namespace: this scope is searched first, so Should() binds to AwesomeAssertions, not to TUnit's global Should().
using AwesomeAssertions;

// The fakes read fields the tests set; TUnit creates a new instance for every test, as xUnit did.
#pragma warning disable TUnit0018

/// <summary>
/// The server's addresses must survive a lookup the kill switch blocks, or the server drops out
/// of TUN's bypass routes and of the kill switch's exceptions in the middle of a mode switch.
/// </summary>
[NotInParallel("SvoRayKillSwitch")]
public class SvoRayServerAddressesTests : IDisposable
{
    private static readonly IPAddress First = IPAddress.Parse("45.0.2.10");
    private static readonly IPAddress Second = IPAddress.Parse("45.0.2.11");
    private int _lookups;

    public SvoRayServerAddressesTests() => SvoRayServerAddresses.Reset();

    public void Dispose() => SvoRayServerAddresses.Reset();

    private Func<string, Task<IPAddress[]>> Answer(params IPAddress[] addresses) => _ =>
    {
        _lookups++;
        return Task.FromResult(addresses);
    };

    private Func<string, Task<IPAddress[]>> Fail() => _ =>
    {
        _lookups++;
        throw new TimeoutException();
    };

    [Test]
    public async Task ResolveAsync_ShouldMakeTheAnswerCurrent()
    {
        var found = await SvoRayServerAddresses.ResolveAsync("vpn.example.com", Answer(First), preferKnown: false);

        found.Should().Equal(First);
        SvoRayServerAddresses.Current.Should().Equal(First);
    }

    [Test]
    public async Task ResolveAsync_WhenTheLookupFails_ShouldKeepTheLastAnswer()
    {
        await SvoRayServerAddresses.ResolveAsync("vpn.example.com", Answer(First), preferKnown: false);

        var found = await SvoRayServerAddresses.ResolveAsync("vpn.example.com", Fail(), preferKnown: false);

        found.Should().Equal(First);
        SvoRayServerAddresses.Current.Should().Equal(First);
    }

    [Test]
    public async Task ResolveAsync_WhileTheKillSwitchHolds_ShouldNotWaitForALookup()
    {
        await SvoRayServerAddresses.ResolveAsync("vpn.example.com", Answer(First), preferKnown: false);
        _lookups = 0;

        var found = await SvoRayServerAddresses.ResolveAsync("VPN.example.com", Fail(), preferKnown: true);

        found.Should().Equal(First);
        _lookups.Should().Be(0, "Windows' DNS is blocked; asking would only cost the 5-second timeout");
    }

    [Test]
    public async Task ResolveAsync_WithoutTheKillSwitch_ShouldAskAgainAndFollowANewAddress()
    {
        await SvoRayServerAddresses.ResolveAsync("vpn.example.com", Answer(First), preferKnown: false);

        var found = await SvoRayServerAddresses.ResolveAsync("vpn.example.com", Answer(Second), preferKnown: false);

        found.Should().Equal(Second);
    }

    [Test]
    public async Task ResolveAsync_ShouldTakeALiteralAddressAsItIs()
    {
        var found = await SvoRayServerAddresses.ResolveAsync(" 45.0.2.10 ", Fail(), preferKnown: false);

        found.Should().Equal(First);
        _lookups.Should().Be(0);
    }

    [Test]
    public async Task ResolveAsync_ForAnotherServerWithNoAnswer_ShouldNotKeepTheOldOne()
    {
        await SvoRayServerAddresses.ResolveAsync("vpn.example.com", Answer(First), preferKnown: false);

        var found = await SvoRayServerAddresses.ResolveAsync("other.example.com", Fail(), preferKnown: true);

        found.Should().BeEmpty();
        SvoRayServerAddresses.Current.Should().BeEmpty();
    }
}
