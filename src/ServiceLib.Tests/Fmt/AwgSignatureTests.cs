using AwesomeAssertions;
using Xunit;

namespace ServiceLib.Tests.Fmt;

public class AwgSignatureTests
{
    [Fact]
    public void TryParse_GeneratorPacket_ShouldKeepFixedBytesAndRandomizeTheRest()
    {
        // The shape WARP_Generator writes: fixed QUIC header bytes, then random bytes, alternating.
        const string spec = "<b 0xc700000001080102030405060708><r 32><b 0x0d0e><r 16>";

        AwgSignature.TryParse(spec, out var signature).Should().BeTrue();
        signature!.Length.Should().Be(14 + 32 + 2 + 16);

        var first = signature.Build();
        var second = signature.Build();

        first.Should().HaveCount(64);
        first[..14].Should().Equal(Convert.FromHexString("c700000001080102030405060708"));
        first[46..48].Should().Equal(0x0d, 0x0e);
        first[14..46].Should().NotEqual(second[14..46], "random parts are drawn anew for every handshake");
    }

    [Fact]
    public void Build_RandomLettersDigitsAndTimestamp_ShouldFollowTheirAlphabets()
    {
        AwgSignature.TryParse("<rc 20><rd 10><t>", out var signature).Should().BeTrue();

        var packet = signature!.Build();

        packet.Should().HaveCount(34);
        packet[..20].Should().OnlyContain(b => char.IsAsciiLetter((char)b));
        packet[20..30].Should().OnlyContain(b => char.IsAsciiDigit((char)b));
        var timestamp = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(packet.AsSpan(30));
        ((long)timestamp).Should().BeCloseTo(DateTimeOffset.UtcNow.ToUnixTimeSeconds(), 5);
    }

    [Theory]
    [InlineData("")]
    [InlineData("no tags at all")]
    [InlineData("<b 0x0102")]
    [InlineData("<b 0x010>")]
    [InlineData("<b 0xzz>")]
    [InlineData("<r abc>")]
    [InlineData("<r 0>")]
    [InlineData("<r 70000>")]
    [InlineData("<q 4>")]
    [InlineData("<>")]
    [InlineData("<d>")]
    public void TryParse_Malformed_ShouldFail(string spec)
    {
        AwgSignature.TryParse(spec, out var signature).Should().BeFalse();
        signature.Should().BeNull();
    }

    [Fact]
    public void BuildPrelude_ShouldSendSpecialPacketsFirstThenJunk()
    {
        var extra = new ProtocolExtraItem
        {
            AwgJc = 4,
            AwgJmin = 40,
            AwgJmax = 70,
            AwgSpecialPackets = ["<b 0xaa><r 9>", "<b 0xbb><r 19>"],
        };

        var decoys = AwgDecoys.From(extra);
        decoys.Should().NotBeNull();

        var prelude = decoys!.BuildPrelude();

        prelude.Should().HaveCount(6);
        prelude[0].Should().HaveCount(10);
        prelude[0][0].Should().Be(0xaa);
        prelude[1].Should().HaveCount(20);
        prelude[1][0].Should().Be(0xbb);
        prelude.Skip(2).Should().OnlyContain(junk => junk.Length >= 40 && junk.Length <= 70);
    }

    [Fact]
    public void From_ProfileWithoutDecoys_ShouldBeNull()
    {
        AwgDecoys.From(new ProtocolExtraItem()).Should().BeNull();
        AwgDecoys.From(new ProtocolExtraItem { AwgJc = 4, AwgJmin = 0, AwgJmax = 0 }).Should().BeNull();
    }

    [Fact]
    public void IsHandshakeInitiation_ShouldLookAtTypeAndLengthOnly()
    {
        var initiation = new byte[148];
        initiation[0] = 1;
        // WARP-style reserved bytes after the type must not hide an initiation.
        initiation[1] = 7;
        initiation[2] = 8;
        initiation[3] = 9;
        AwgDecoys.IsHandshakeInitiation(initiation).Should().BeTrue();

        var response = new byte[92];
        response[0] = 2;
        AwgDecoys.IsHandshakeInitiation(response).Should().BeFalse();

        var transport = new byte[148];
        transport[0] = 4;
        AwgDecoys.IsHandshakeInitiation(transport).Should().BeFalse();
    }
}
