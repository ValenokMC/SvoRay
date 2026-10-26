using AwesomeAssertions;
using ServiceLib.Tests.CoreConfig;
using Xunit;

namespace ServiceLib.Tests.Services;

/// <summary>
/// Runs the relay against a stand-in WireGuard server on loopback. Kept in one class with the
/// manager tests, because <see cref="AwgRelayManager"/> is a process-wide singleton.
/// </summary>
public class AwgRelayTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private static AwgDecoys Decoys(int jc = 2) => new(jc, 10, 10,
        [Parse("<b 0xdeadbeef>"), Parse("<b 0xfeed><r 2>")]);

    private static AwgSignature Parse(string spec)
    {
        AwgSignature.TryParse(spec, out var signature).Should().BeTrue();
        return signature!;
    }

    private static byte[] Message(byte type, int length)
    {
        var packet = new byte[length];
        packet[0] = type;
        RandomNumberGenerator.Fill(packet.AsSpan(4));
        return packet;
    }

    private static async Task<byte[]> ReceiveAsync(UdpClient socket)
    {
        using var cts = new CancellationTokenSource(Timeout);
        var result = await socket.ReceiveAsync(cts.Token);
        return result.Buffer;
    }

    [Fact]
    public async Task Relay_HandshakeInitiation_ShouldBePrecededByDecoysInOrder()
    {
        using var server = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var serverPort = ((IPEndPoint)server.Client.LocalEndPoint!).Port;
        using var relay = new AwgRelay("127.0.0.1", serverPort, Decoys(), null);
        using var core = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var relayEndPoint = new IPEndPoint(IPAddress.Loopback, relay.LocalPort);

        var initiation = Message(1, 148);
        await core.SendAsync(initiation, relayEndPoint);

        (await ReceiveAsync(server)).Should().Equal(0xde, 0xad, 0xbe, 0xef);
        var second = await ReceiveAsync(server);
        second.Should().HaveCount(4);
        second[..2].Should().Equal(0xfe, 0xed);
        (await ReceiveAsync(server)).Should().HaveCount(10);
        (await ReceiveAsync(server)).Should().HaveCount(10);
        (await ReceiveAsync(server)).Should().Equal(initiation);
    }

    [Fact]
    public async Task Relay_ShouldPassOtherTrafficUnchangedBothWays()
    {
        using var server = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var serverPort = ((IPEndPoint)server.Client.LocalEndPoint!).Port;
        using var relay = new AwgRelay("localhost", serverPort, Decoys(), null);
        using var core = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var relayEndPoint = new IPEndPoint(IPAddress.Loopback, relay.LocalPort);

        var transport = Message(4, 96);
        await core.SendAsync(transport, relayEndPoint);

        using var cts = new CancellationTokenSource(Timeout);
        var received = await server.ReceiveAsync(cts.Token);
        received.Buffer.Should().Equal(transport, "only a handshake initiation gets decoys");

        var response = Message(2, 92);
        await server.SendAsync(response, received.RemoteEndPoint);

        (await ReceiveAsync(core)).Should().Equal(response);
    }

    [Fact]
    public void Manager_ShouldRelayOnlyProfilesWithDecoysAndReuseRelays()
    {
        var manager = AwgRelayManager.Instance;
        try
        {
            var plain = WireguardNode(withDecoys: false);
            manager.ResolveEndpoint(plain, null).Should().Be(("198.51.100.7", 2408));

            var warp = WireguardNode(withDecoys: true);
            var first = manager.ResolveEndpoint(warp, null);
            first.Address.Should().Be("127.0.0.1");
            first.Port.Should().BePositive();
            manager.ResolveEndpoint(warp, null).Should().Be(first, "a regenerated config keeps the running relay");

            manager.MarkStale();
            manager.ResolveEndpoint(warp, null);
            manager.StopStale();
            manager.Count.Should().Be(1, "a relay the new config asked for again stays");

            manager.MarkStale();
            manager.StopStale();
            manager.Count.Should().Be(0);
        }
        finally
        {
            manager.StopAll();
        }
    }

    [Fact]
    public void GenerateConfig_BothCores_ShouldPointWireguardAtTheRelay()
    {
        var manager = AwgRelayManager.Instance;
        try
        {
            var node = WireguardNode(withDecoys: true);

            var singboxConfig = CoreConfigTestFactory.CreateConfig(ECoreType.sing_box);
            CoreConfigTestFactory.BindAppManagerConfig(singboxConfig);
            var singboxContext = CoreConfigTestFactory.CreateContext(singboxConfig, node, ECoreType.sing_box);
            var singboxResult = new CoreConfigSingboxService(singboxContext).GenerateClientConfigContent();
            singboxResult.Success.Should().BeTrue($"ret msg: {singboxResult.Msg}");
            var singbox = JsonUtils.Deserialize<SingboxConfig>(singboxResult.Data!.ToString())!;
            var peer = singbox.endpoints!.Single(e => e.type == "wireguard").peers!.Single();

            var xrayConfig = CoreConfigTestFactory.CreateConfig();
            // As in SvoRay's TUN mode: the core is pinned to the physical adapter.
            xrayConfig.CoreBasicItem.SendThrough = "192.0.2.10";
            xrayConfig.CoreBasicItem.BindInterface = "Ethernet";
            CoreConfigTestFactory.BindAppManagerConfig(xrayConfig);
            var xrayContext = CoreConfigTestFactory.CreateContext(xrayConfig, node, ECoreType.Xray);
            var xrayResult = new CoreConfigV2rayService(xrayContext).GenerateClientConfigContent();
            xrayResult.Success.Should().BeTrue($"ret msg: {xrayResult.Msg}");
            var xray = JsonUtils.Deserialize<V2rayConfig>(xrayResult.Data!.ToString())!;
            var outbound = xray.outbounds.Single(o => o.protocol == "wireguard");

            peer.address.Should().Be("127.0.0.1");
            peer.port.Should().BePositive();
            outbound.settings.peers!.Single().endpoint.Should().StartWith("127.0.0.1:");
            outbound.sendThrough.Should().BeNull("the relay, not the core, leaves through the adapter");
            outbound.streamSettings?.sockopt?.Interface.Should().BeNull();
        }
        finally
        {
            manager.StopAll();
        }
    }

    private static ProfileItem WireguardNode(bool withDecoys)
    {
        var node = new ProfileItem
        {
            IndexId = withDecoys ? "warp-awg" : "warp-plain",
            ConfigType = EConfigType.WireGuard,
            CoreType = ECoreType.sing_box,
            Remarks = "Cloudflare WARP",
            Address = "198.51.100.7",
            Port = 2408,
            Password = "private-key",
            Subid = string.Empty,
        };
        node.SetProtocolExtra(new ProtocolExtraItem
        {
            WgPublicKey = WireguardFmt.WarpPublicKey,
            WgInterfaceAddress = "172.16.0.2/32",
            WgMtu = 1280,
            AwgJc = withDecoys ? 4 : null,
            AwgJmin = withDecoys ? 40 : null,
            AwgJmax = withDecoys ? 70 : null,
        });
        return node;
    }
}
