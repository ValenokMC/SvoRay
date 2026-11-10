namespace ServiceLib.Common;

/// <summary>
/// Providers put a flag emoji in front of a server's name. Windows draws such an emoji as two
/// letters ("DE", "FI"), so the screen takes the flag out of the text and shows a picture of it.
/// A flag emoji is a pair of regional indicator symbols, one per letter of the country code.
/// </summary>
public static class SvoRayFlag
{
    private const int IndicatorA = 0x1F1E6;
    private const int IndicatorZ = 0x1F1FF;

    /// <summary>
    /// The name without its first flag, and that flag's lower-case country code ("de"); the name
    /// comes back untouched, with a null code, when it holds no flag.
    /// </summary>
    public static (string? Code, string Text) Split(string? name)
    {
        if (name.IsNullOrEmpty())
        {
            return (null, name ?? string.Empty);
        }

        for (var i = 0; i + 3 < name!.Length; i++)
        {
            if (!TryIndicator(name, i, out var first) || !TryIndicator(name, i + 2, out var second))
            {
                continue;
            }

            var code = new string([(char)('a' + first), (char)('a' + second)]);
            var text = (name[..i] + name[(i + 4)..]).Trim();
            return (code, text.Length == 0 ? name : text);
        }
        return (null, name);
    }

    private static bool TryIndicator(string text, int index, out int letter)
    {
        letter = -1;
        if (index + 1 >= text.Length || !char.IsHighSurrogate(text[index]) || !char.IsLowSurrogate(text[index + 1]))
        {
            return false;
        }

        var scalar = char.ConvertToUtf32(text[index], text[index + 1]);
        if (scalar < IndicatorA || scalar > IndicatorZ)
        {
            return false;
        }
        letter = scalar - IndicatorA;
        return true;
    }
}
