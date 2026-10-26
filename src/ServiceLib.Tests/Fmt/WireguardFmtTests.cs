using AwesomeAssertions;
using ServiceLib.Handler.Fmt;
using Xunit;

namespace ServiceLib.Tests.Fmt;

public class WireguardFmtTests
{
    [Fact]
    public void ResolveConfig_ShouldParsePeersAndIgnoreInlineComments()
    {
        const string config =
            """
            [Interface]
            PrivateKey = interface-private-key
            Address = 10.0.0.2/32, fd00::2/128 ; inline comment
            MTU = 1420

            [Peer]
            PublicKey = peer-public-key
            PresharedKey = peer-preshared-key
            Reserved = 1, 2, 3 # inline comment
            Endpoint = [2001:db8::1]:51820 # inline comment

            [Peer]
            PublicKey = peer-public-key-2
            Endpoint = example.com:12345
            """;

        var resolved = WireguardFmt.ResolveConfig(config);

        resolved.Should().NotBeNull();
        resolved.Should().HaveCount(2);

        var first = resolved![0];
        first.Address.Should().Be("2001:db8::1");
        first.Port.Should().Be(51820);
        first.Password.Should().Be("interface-private-key");
        first.GetProtocolExtra().WgReserved.Should().Be("1, 2, 3");
        first.GetProtocolExtra().WgInterfaceAddress.Should().Be("10.0.0.2/32, fd00::2/128");
        first.GetProtocolExtra().WgMtu.Should().Be(1420);

        var second = resolved[1];
        second.Address.Should().Be("example.com");
        second.Port.Should().Be(12345);
    }

    // The shape WARP_Generator produces: plain WireGuard to Cloudflare plus client-side decoys.
    private const string WarpAmneziaConfig =
        """
        [Interface]
        PrivateKey = warp-private-key
        Jc = 4
        Jmin = 40
        Jmax = 70
        H1 = 1
        H2 = 2
        H3 = 3
        H4 = 4
        I1 = <b 0xc70000000108ce1bfd4f><r 30><b 0x0001><r 8>
        I2 = <b 0xcf00000001><r 40>
        Address = 172.16.0.2, 2606:4700:110:8a36::1
        DNS = 8.8.8.8, 8.8.4.4, 2001:4860:4860::8888, 2001:4860:4860::8844
        MTU = 1280

        [Peer]
        PublicKey = bmXOC+F1FxEMF9dyiK2H5/1SUtzH0JuVo51h2wPfgyo=
        AllowedIPs = 0.0.0.0/0, ::/0
        Endpoint = engage.cloudflareclient.com:4500
        """;

    [Fact]
    public void ResolveConfig_WarpAmneziaConfig_ShouldKeepDecoysForTheRelay()
    {
        var resolved = WireguardFmt.ResolveConfig(WarpAmneziaConfig, out var unsupported);

        unsupported.Should().BeFalse();
        resolved.Should().ContainSingle();

        var item = resolved![0];
        item.Remarks.Should().Be("Cloudflare WARP");
        item.Address.Should().Be("engage.cloudflareclient.com");
        item.Port.Should().Be(4500);
        item.Password.Should().Be("warp-private-key");

        var extra = item.GetProtocolExtra();
        extra.WgPublicKey.Should().Be(WireguardFmt.WarpPublicKey);
        extra.WgInterfaceAddress.Should().Be("172.16.0.2, 2606:4700:110:8a36::1");
        extra.WgMtu.Should().Be(1280);
        extra.AwgJc.Should().Be(4);
        extra.AwgJmin.Should().Be(40);
        extra.AwgJmax.Should().Be(70);
        extra.AwgSpecialPackets.Should().Equal(
            "<b 0xc70000000108ce1bfd4f><r 30><b 0x0001><r 8>",
            "<b 0xcf00000001><r 40>");
    }

    [Fact]
    public void ResolveConfig_DecoysShouldSurviveStorage()
    {
        var item = WireguardFmt.ResolveConfig(WarpAmneziaConfig)![0];

        var restored = new ProfileItem { ProtoExtra = item.ProtoExtra };

        AwgDecoys.From(restored.GetProtocolExtra())!.SpecialPackets.Should().HaveCount(2);
    }

    [Fact]
    public void ResolveConfig_PlainWireguard_ShouldCarryNoDecoys()
    {
        const string config =
            """
            [Interface]
            PrivateKey = key
            Address = 10.0.0.2/32

            [Peer]
            PublicKey = peer
            Endpoint = 198.51.100.7:51820
            """;

        var item = WireguardFmt.ResolveConfig(config, out var unsupported)![0];

        unsupported.Should().BeFalse();
        item.Remarks.Should().Be("WireGuard Peer 1");
        AwgDecoys.From(item.GetProtocolExtra()).Should().BeNull();
    }

    [Theory]
    [InlineData("H1 = 1", "H1 = 123456")]
    [InlineData("H4 = 4", "H4 = 100-200")]
    [InlineData("Jc = 4", "Jc = 4\nS1 = 15")]
    [InlineData("Jc = 4", "Jc = 4\nS2 = 20")]
    [InlineData("Jmax = 70", "Jmax = 30")]
    [InlineData("Jc = 4", "Jc = 500")]
    [InlineData("I2 = <b 0xcf00000001><r 40>", "I2 = <b 0xcf0000000><r 40>")]
    public void ResolveConfig_NeedsAmneziaServerOrUnreadable_ShouldBeRefused(string original, string replacement)
    {
        var config = WarpAmneziaConfig.Replace(original, replacement);

        var resolved = WireguardFmt.ResolveConfig(config, out var unsupported);

        resolved.Should().BeNull();
        unsupported.Should().BeTrue();
    }

    [Fact]
    public void ResolveConfig_ZeroPaddingAndDefaultHeaders_ShouldBeAccepted()
    {
        var config = WarpAmneziaConfig.Replace("Jc = 4", "Jc = 4\nS1 = 0\nS2 = 0");

        WireguardFmt.ResolveConfig(config, out var unsupported).Should().ContainSingle();
        unsupported.Should().BeFalse();
    }
}
