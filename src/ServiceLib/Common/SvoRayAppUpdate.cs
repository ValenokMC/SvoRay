namespace ServiceLib.Common;

/// <summary>A stable SvoRay release that is newer than the running build and can be installed.</summary>
public sealed record SvoRayRelease(Version Version, string Tag, string NotesUrl, Uri SetupUrl, string SetupName, Uri SumsUrl);

/// <summary>What a look at the latest release concluded.</summary>
public enum ESvoRayUpdateStatus
{
    /// <summary>The running build is the latest stable release, or newer than it.</summary>
    UpToDate,

    /// <summary>A newer stable release with an installer and a checksum file.</summary>
    Available,

    /// <summary>The answer cannot be used: not a release, a prerelease or draft, or files missing.</summary>
    Unusable,
}

public sealed record SvoRayUpdateCheck(ESvoRayUpdateStatus Status, SvoRayRelease? Release = null, string? Reason = null);

/// <summary>
/// Self-update of SvoRay from its own GitHub releases. v2rayN's updater is not used: it
/// expects v2rayN's zip and its separate upgrade program, neither of which SvoRay ships.
/// Everything here is pure, so the decisions are covered by tests without a network.
/// </summary>
public static class SvoRayAppUpdate
{
    public const string Repository = "ValenokMC/SvoRay";
    public const string LatestReleaseApiUrl = $"https://api.github.com/repos/{Repository}/releases/latest";
    public const string ReleasesPageUrl = $"https://github.com/{Repository}/releases";
    public const string SumsFileName = "SHA256SUMS.txt";

    /// <summary>A day between automatic checks, as asked; a click checks at any time.</summary>
    public static readonly TimeSpan CheckInterval = TimeSpan.FromDays(1);

    private const string DownloadPrefix = $"https://github.com/{Repository}/releases/download/";

    /// <summary>
    /// Reads "v0.5.0", "0.5.0" or "0.5" as a three-part version. A suffix such as "-beta" means a
    /// test build and gives null, as does anything that is not a plain version.
    /// </summary>
    public static Version? ParseVersion(string? text)
    {
        var value = text?.Trim();
        if (value.IsNullOrEmpty())
        {
            return null;
        }
        if (value![0] is 'v' or 'V')
        {
            value = value[1..];
        }

        var parts = value.Split('.');
        if (parts.Length is < 2 or > 4)
        {
            return null;
        }

        var numbers = new int[3];
        for (var i = 0; i < parts.Length; i++)
        {
            if (parts[i].Length == 0 || !parts[i].All(char.IsAsciiDigit) || !int.TryParse(parts[i], out var number))
            {
                return null;
            }
            // A fourth part (an assembly revision) carries no release meaning and is dropped.
            if (i < 3)
            {
                numbers[i] = number;
            }
        }
        return new Version(numbers[0], numbers[1], numbers[2]);
    }

    /// <summary>The running build as Major.Minor.Build, which is how releases are tagged.</summary>
    public static Version Normalize(Version version)
    {
        return new Version(version.Major, version.Minor, Math.Max(version.Build, 0));
    }

    /// <summary>
    /// Decides from the body of <c>/releases/latest</c> whether to offer an update. The installer
    /// and the checksum file must both be assets of this very release, served from this
    /// repository's download path - a body that points anywhere else is not trusted.
    /// </summary>
    public static SvoRayUpdateCheck Evaluate(string? json, Version current)
    {
        if (json.IsNullOrEmpty())
        {
            return new(ESvoRayUpdateStatus.Unusable, Reason: "empty answer");
        }

        try
        {
            using var document = JsonDocument.Parse(json!);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return new(ESvoRayUpdateStatus.Unusable, Reason: "not a release");
            }

            if (GetBool(root, "draft") || GetBool(root, "prerelease"))
            {
                return new(ESvoRayUpdateStatus.Unusable, Reason: "draft or prerelease");
            }

            var tag = GetString(root, "tag_name");
            var version = ParseVersion(tag);
            if (version is null)
            {
                return new(ESvoRayUpdateStatus.Unusable, Reason: "tag is not a version");
            }

            if (version <= Normalize(current))
            {
                return new(ESvoRayUpdateStatus.UpToDate);
            }

            var setupName = $"SvoRay-{version.ToString(3)}-setup.exe";
            var setupUrl = FindAsset(root, tag!, setupName);
            var sumsUrl = FindAsset(root, tag!, SumsFileName);
            if (setupUrl is null || sumsUrl is null)
            {
                return new(ESvoRayUpdateStatus.Unusable, Reason: "installer or checksums missing");
            }

            var notesUrl = GetString(root, "html_url");
            if (notesUrl is null || !notesUrl.StartsWith($"https://github.com/{Repository}/releases/", StringComparison.Ordinal))
            {
                notesUrl = $"{ReleasesPageUrl}/tag/{Uri.EscapeDataString(tag!)}";
            }

            return new(ESvoRayUpdateStatus.Available,
                new SvoRayRelease(version, tag!, notesUrl, setupUrl, setupName, sumsUrl));
        }
        catch (JsonException)
        {
            return new(ESvoRayUpdateStatus.Unusable, Reason: "malformed answer");
        }
    }

    /// <summary>
    /// The expected SHA-256 of <paramref name="fileName"/> from a <c>sha256sum</c>-style list
    /// ("HEX  name" or "HEX *name"); null when the file is not listed or the hash is malformed.
    /// </summary>
    public static string? FindExpectedHash(string? sums, string fileName)
    {
        if (sums.IsNullOrEmpty())
        {
            return null;
        }

        foreach (var rawLine in sums!.Split('\n'))
        {
            var line = rawLine.Trim();
            var space = line.IndexOfAny([' ', '\t']);
            if (space != 64)
            {
                continue;
            }

            var name = line[space..].Trim().TrimStart('*');
            var hash = line[..space];
            if (string.Equals(name, fileName, StringComparison.Ordinal) && hash.All(char.IsAsciiHexDigit))
            {
                return hash.ToLowerInvariant();
            }
        }
        return null;
    }

    /// <summary>Hashes the open stream from its start and compares with the expected value.</summary>
    public static bool HashMatches(Stream content, string? expectedHash)
    {
        if (expectedHash is not { Length: 64 })
        {
            return false;
        }

        content.Position = 0;
        var actual = Convert.ToHexStringLower(SHA256.HashData(content));
        content.Position = 0;
        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(actual), Encoding.ASCII.GetBytes(expectedHash.ToLowerInvariant()));
    }

    /// <summary>
    /// Inno Setup switches for an update the user has already confirmed in SvoRay: no wizard
    /// pages to click through again, but a progress window so the switch is visible, no message
    /// boxes, and <c>/LAUNCH=1</c> so the installer starts the new version when it is done.
    /// </summary>
    public static string InstallerArguments => "/SILENT /SUPPRESSMSGBOXES /NORESTART /LAUNCH=1";

    private static Uri? FindAsset(JsonElement root, string tag, string name)
    {
        if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var expected = $"{DownloadPrefix}{Uri.EscapeDataString(tag)}/{Uri.EscapeDataString(name)}";
        foreach (var asset in assets.EnumerateArray())
        {
            if (asset.ValueKind == JsonValueKind.Object
                && string.Equals(GetString(asset, "name"), name, StringComparison.Ordinal)
                && string.Equals(GetString(asset, "browser_download_url"), expected, StringComparison.Ordinal))
            {
                return new Uri(expected);
            }
        }
        return null;
    }

    private static string? GetString(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static bool GetBool(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
    }
}
