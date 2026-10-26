namespace ServiceLib.Common;

/// <summary>
/// The AmneziaWG packets a client sends ahead of each WireGuard handshake initiation: the
/// special packets I1-I5 in order, then <see cref="Jc"/> junk packets of
/// <see cref="Jmin"/>-<see cref="Jmax"/> random bytes, as amneziawg-go does. A plain WireGuard
/// server drops all of them, so they only change what DPI sees on the wire.
/// </summary>
public sealed record AwgDecoys(int Jc, int Jmin, int Jmax, IReadOnlyList<AwgSignature> SpecialPackets)
{
    /// <summary>Message type 1, which is always 148 bytes long.</summary>
    private const int HandshakeInitiationLength = 148;

    /// <summary>The decoys stored in a profile, or null when it has none to send.</summary>
    public static AwgDecoys? From(ProtocolExtraItem extra)
    {
        var specialPackets = new List<AwgSignature>();
        foreach (var spec in extra.AwgSpecialPackets ?? [])
        {
            if (AwgSignature.TryParse(spec, out var signature))
            {
                specialPackets.Add(signature!);
            }
        }

        var jc = extra.AwgJc ?? 0;
        var jmin = extra.AwgJmin ?? 0;
        var jmax = extra.AwgJmax ?? 0;
        if (jmin < 1 || jmax < jmin)
        {
            jc = 0;
        }

        return jc > 0 || specialPackets.Count > 0
            ? new AwgDecoys(jc, jmin, jmax, specialPackets)
            : null;
    }

    /// <summary>
    /// True for a WireGuard handshake initiation. Only the type byte is checked: some clients
    /// (Cloudflare WARP among them) put their "reserved" bytes in the next three.
    /// </summary>
    public static bool IsHandshakeInitiation(ReadOnlySpan<byte> packet)
    {
        return packet.Length == HandshakeInitiationLength && packet[0] == 1;
    }

    /// <summary>A fresh set of decoys, in the order they go out.</summary>
    public List<byte[]> BuildPrelude()
    {
        var packets = new List<byte[]>(SpecialPackets.Count + Jc);
        packets.AddRange(SpecialPackets.Select(signature => signature.Build()));
        for (var i = 0; i < Jc; i++)
        {
            var junk = new byte[RandomNumberGenerator.GetInt32(Jmin, Jmax + 1)];
            RandomNumberGenerator.Fill(junk);
            packets.Add(junk);
        }
        return packets;
    }
}
