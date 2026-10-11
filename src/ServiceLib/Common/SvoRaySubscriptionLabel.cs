namespace ServiceLib.Common;

/// <summary>
/// The short name a subscription goes by on the simple screen, so that with several of them
/// each server says where it came from. Panels that serve Happ send their name in the
/// <c>profile-title</c> header, plain or as <c>base64:...</c>; v2rayN itself names every
/// imported subscription "import_sub", which tells the user nothing.
/// </summary>
public static class SvoRaySubscriptionLabel
{
    public const string HeaderName = "profile-title";
    private const string DefaultRemarks = "import_sub";
    private const int MaxLength = 32;

    /// <summary>The header's title, decoded and cleaned; null when missing or unusable.</summary>
    public static string? ParseTitle(string? header)
    {
        var value = header?.Trim();
        if (value.IsNullOrEmpty() || value!.Length > 1024)
        {
            return null;
        }

        if (value.StartsWith("base64:", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                value = Encoding.UTF8.GetString(Convert.FromBase64String(value["base64:".Length..].Trim()));
            }
            catch (FormatException)
            {
                return null;
            }
        }

        // Control characters and line breaks would break the one-line selector.
        var clean = new string(value.Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (clean.Length == 0)
        {
            return null;
        }
        return clean.Length <= MaxLength ? clean : clean[..(MaxLength - 1)].TrimEnd() + "…";
    }

    /// <summary>
    /// A name the user gave in advanced mode wins, then the provider's title, then the host of
    /// the link - never the whole URL, which is private.
    /// </summary>
    public static string For(SubItem sub)
    {
        if (sub.Remarks.IsNotEmpty() && sub.Remarks != DefaultRemarks)
        {
            return sub.Remarks;
        }
        if (sub.ProfileTitle.IsNotEmpty())
        {
            return sub.ProfileTitle!;
        }
        return Uri.TryCreate(sub.Url?.Trim(), UriKind.Absolute, out var uri) && uri.Host.IsNotEmpty()
            ? uri.IdnHost
            : sub.Remarks.IsNotEmpty() ? sub.Remarks : "—";
    }

    /// <summary>"Provider · Server" when servers come from more than one place, the bare name otherwise.</summary>
    public static string Compose(string? sourceLabel, string serverName, bool severalSources)
    {
        return severalSources && sourceLabel.IsNotEmpty() ? $"{sourceLabel} · {serverName}" : serverName;
    }
}
