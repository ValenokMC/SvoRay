using System.Buffers.Binary;
using System.Globalization;

namespace ServiceLib.Common;

/// <summary>
/// One AmneziaWG special packet (I1-I5) in the tag format amneziawg-go reads, for example
/// <c>&lt;b 0xc700000001&gt;&lt;r 16&gt;&lt;t&gt;</c>: fixed bytes, random bytes, random letters,
/// random digits and a timestamp, in the order written. Text between tags is ignored. The
/// packet is built anew for every handshake, so its random parts never repeat.
/// </summary>
public sealed class AwgSignature
{
    /// <summary>The largest payload one UDP datagram can carry.</summary>
    public const int MaxLength = 65507;

    private const string Letters = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private const string Digits = "0123456789";

    private enum SegmentKind
    {
        Bytes,
        Random,
        RandomLetters,
        RandomDigits,
        Timestamp,
    }

    private sealed record Segment(SegmentKind Kind, int Length, byte[]? Data = null);

    private readonly List<Segment> _segments;

    private AwgSignature(List<Segment> segments)
    {
        _segments = segments;
    }

    public int Length => _segments.Sum(segment => segment.Length);

    /// <summary>
    /// Reads <paramref name="spec"/>. Returns false for an empty spec, an unknown or malformed
    /// tag, or a packet that would not fit in one datagram. The data tags (d, ds, dz) shape the
    /// payload of real packets and have no meaning in a decoy, so they are refused too.
    /// </summary>
    public static bool TryParse(string? spec, out AwgSignature? signature)
    {
        signature = null;
        if (spec.IsNullOrEmpty())
        {
            return false;
        }

        var segments = new List<Segment>();
        var position = 0;
        while (true)
        {
            var open = spec!.IndexOf('<', position);
            if (open < 0)
            {
                break;
            }
            var close = spec.IndexOf('>', open + 1);
            if (close < 0)
            {
                return false;
            }
            position = close + 1;

            var fields = spec[(open + 1)..close].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length == 0)
            {
                return false;
            }
            var value = fields.Length > 1 ? fields[1] : string.Empty;

            Segment? segment = fields[0] switch
            {
                "b" => ParseBytes(value),
                "r" => ParseLength(SegmentKind.Random, value),
                "rc" => ParseLength(SegmentKind.RandomLetters, value),
                "rd" => ParseLength(SegmentKind.RandomDigits, value),
                "t" => new Segment(SegmentKind.Timestamp, 4),
                _ => null,
            };
            if (segment is null)
            {
                return false;
            }
            segments.Add(segment);
        }

        if (segments.Count == 0)
        {
            return false;
        }

        var result = new AwgSignature(segments);
        if (result.Length is <= 0 or > MaxLength)
        {
            return false;
        }

        signature = result;
        return true;
    }

    public byte[] Build()
    {
        var packet = new byte[Length];
        var offset = 0;
        foreach (var segment in _segments)
        {
            var target = packet.AsSpan(offset, segment.Length);
            switch (segment.Kind)
            {
                case SegmentKind.Bytes:
                    segment.Data.CopyTo(target);
                    break;

                case SegmentKind.Random:
                    RandomNumberGenerator.Fill(target);
                    break;

                case SegmentKind.RandomLetters:
                    FillFrom(target, Letters);
                    break;

                case SegmentKind.RandomDigits:
                    FillFrom(target, Digits);
                    break;

                case SegmentKind.Timestamp:
                    BinaryPrimitives.WriteUInt32BigEndian(target, (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                    break;
            }
            offset += segment.Length;
        }
        return packet;
    }

    private static Segment? ParseBytes(string value)
    {
        var hex = value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? value[2..] : value;
        if (hex.Length == 0 || hex.Length % 2 != 0 || hex.Length / 2 > MaxLength)
        {
            return null;
        }
        try
        {
            var data = Convert.FromHexString(hex);
            return new Segment(SegmentKind.Bytes, data.Length, data);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static Segment? ParseLength(SegmentKind kind, string value)
    {
        return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var length)
               && length is > 0 and <= MaxLength
            ? new Segment(kind, length)
            : null;
    }

    private static void FillFrom(Span<byte> target, string alphabet)
    {
        for (var i = 0; i < target.Length; i++)
        {
            target[i] = (byte)alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
        }
    }
}
