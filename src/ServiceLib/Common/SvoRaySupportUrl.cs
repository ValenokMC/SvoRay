namespace ServiceLib.Common;

/// <summary>
/// Validates subscription-provided support links before they can reach the operating system.
/// </summary>
public static class SvoRaySupportUrl
{
    public const string FallbackUrl = "https://github.com/ValenokMC/SvoRay/issues";
    public const int MaxLength = 2048;

    private static readonly HashSet<string> AllowedSchemes = new(StringComparer.OrdinalIgnoreCase)
    {
        Uri.UriSchemeHttp,
        Uri.UriSchemeHttps,
        "tg"
    };

    public static string Resolve(string? candidate)
    {
        return Normalize(candidate) ?? FallbackUrl;
    }

    public static string? Normalize(string? candidate)
    {
        var value = candidate?.Trim();
        if (value.IsNullOrEmpty() || value!.Length > MaxLength || !Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            return null;
        }

        if (!AllowedSchemes.Contains(uri.Scheme) || uri.Host.IsNullOrEmpty())
        {
            return null;
        }

        if (uri.Scheme.Equals("tg", StringComparison.OrdinalIgnoreCase)
            && !value.StartsWith("tg://", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return value;
    }
}
