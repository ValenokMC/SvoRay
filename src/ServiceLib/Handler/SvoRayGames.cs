namespace ServiceLib.Handler;

/// <summary>
/// Finds where games live on this PC, for the simple screen's game mode: everything started from
/// these folders leaves the tunnel, so a match runs with the ping of the user's own connection
/// while Discord and the browser stay on the VPN.
/// </summary>
/// <remarks>
/// Domain rules cannot do this. A game talks to its servers over UDP by address, with no name a
/// rule could match, so the only reliable handle is the process that opened the socket. Xray
/// matches a rule entry ending in <c>/</c> as a folder prefix against the full image path, which
/// covers every game in a library without knowing their executables.
/// <para>
/// ⚠️ The comparison is case-sensitive and against the path as the file system spells it, so
/// every folder is rebuilt from the real directory names: Steam stores library paths lower-cased
/// in its own config, and a rule built from that text would never fire.
/// </para>
/// </remarks>
public static class SvoRayGames
{
    /// <summary>
    /// Launchers and anti-cheat services that run from outside a game folder. Xray compares the
    /// bare name without <c>.exe</c>, case-sensitively, so they are spelled as the files are.
    /// </summary>
    public static IReadOnlyList<string> ProcessNames { get; } =
    [
        "steam.exe",
        "steamwebhelper.exe",
        "steamservice.exe",
        "EpicGamesLauncher.exe",
        "EpicWebHelper.exe",
        "EADesktop.exe",
        "EABackgroundService.exe",
        "Battle.net.exe",
        "RiotClientServices.exe",
        "RiotClientUx.exe",
        "vgc.exe",
        "upc.exe",
        "UbisoftConnect.exe",
        "GalaxyClient.exe",
        "FACEIT.exe",
        "faceitservice.exe",
    ];

    /// <summary>Install roots of launchers whose games sit under them by default.</summary>
    private static readonly string[] KnownRoots =
    [
        @"%ProgramFiles%\Epic Games",
        @"%ProgramFiles(x86)%\Epic Games",
        @"%ProgramFiles%\EA Games",
        @"%ProgramFiles%\Electronic Arts",
        @"%ProgramFiles(x86)%\Origin Games",
        @"%ProgramFiles(x86)%\Battle.net",
        @"%ProgramFiles(x86)%\Ubisoft\Ubisoft Game Launcher\games",
        @"%ProgramFiles(x86)%\GOG Galaxy\Games",
        @"%SystemDrive%\Riot Games",
    ];

    /// <summary>
    /// Folder entries for the routing rule, each ending in <c>/</c> and spelled exactly as on
    /// disk. Empty on systems other than Windows.
    /// </summary>
    public static List<string> FindGameFolders()
    {
        if (!OperatingSystem.IsWindows())
        {
            return [];
        }

        var candidates = new List<string>();
        try
        {
            var steam = FindSteamRoot();
            if (steam is not null)
            {
                // Steam itself: the client, its web helper and the default library under it.
                candidates.Add(steam);
                var vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
                if (File.Exists(vdf))
                {
                    candidates.AddRange(ParseLibraryFolders(File.ReadAllText(vdf))
                        .Select(lib => Path.Combine(lib, "steamapps", "common")));
                }
            }

            candidates.AddRange(FindEpicInstallLocations());
            candidates.AddRange(KnownRoots.Select(Environment.ExpandEnvironmentVariables));
        }
        catch (Exception ex)
        {
            Logging.SaveLog("SvoRay game folders", ex);
        }

        return BuildFolderEntries(candidates);
    }

    /// <summary>
    /// Turns candidate paths into rule entries: only folders that exist, in their on-disk
    /// spelling, with forward slashes and a trailing slash, without duplicates or folders
    /// already covered by a parent in the list.
    /// </summary>
    public static List<string> BuildFolderEntries(IEnumerable<string> candidates)
    {
        var entries = candidates
            .Select(ExactCase)
            .Where(path => path is not null)
            .Select(path => path!.Replace('\\', '/').TrimEnd('/') + "/")
            .Distinct(StringComparer.Ordinal)
            .OrderBy(path => path.Length)
            .ToList();

        var result = new List<string>();
        foreach (var entry in entries)
        {
            if (!result.Any(parent => entry.StartsWith(parent, StringComparison.Ordinal)))
            {
                result.Add(entry);
            }
        }
        return result;
    }

    /// <summary>
    /// Library paths from Steam's <c>libraryfolders.vdf</c>: every <c>"path"</c> value, with the
    /// doubled backslashes of the format undone.
    /// </summary>
    public static List<string> ParseLibraryFolders(string? vdf)
    {
        if (vdf.IsNullOrEmpty())
        {
            return [];
        }

        return Regex.Matches(vdf!, "\"path\"\\s*\"((?:[^\"\\\\]|\\\\.)*)\"", RegexOptions.IgnoreCase)
            .Select(m => m.Groups[1].Value.Replace(@"\\", @"\"))
            .Where(path => path.IsNotEmpty())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// The path as the file system spells it, or null when it does not exist. Walks down from
    /// the root and takes each name from the directory listing; the drive letter is upper-cased,
    /// which is how Windows reports image paths.
    /// </summary>
    public static string? ExactCase(string? path)
    {
        if (path.IsNullOrEmpty())
        {
            return null;
        }

        try
        {
            var full = Path.GetFullPath(path!);
            if (!Directory.Exists(full))
            {
                return null;
            }

            var root = Path.GetPathRoot(full)!;
            var current = root.Length >= 2 && root[1] == ':'
                ? char.ToUpperInvariant(root[0]) + root[1..]
                : root;

            var parts = full[root.Length..].Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                var match = Directory.EnumerateDirectories(current)
                    .Select(Path.GetFileName)
                    .FirstOrDefault(name => string.Equals(name, part, StringComparison.OrdinalIgnoreCase));
                if (match is null)
                {
                    return null;
                }
                current = Path.Combine(current, match);
            }
            return current;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
                                       or NotSupportedException)
        {
            return null;
        }
    }

    [SupportedOSPlatform("windows")]
    private static string? FindSteamRoot()
    {
        var candidates = new[]
        {
            Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string,
            Microsoft.Win32.Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", null) as string,
            Microsoft.Win32.Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Valve\Steam", "InstallPath", null) as string,
        };
        return candidates.FirstOrDefault(path => path.IsNotEmpty() && Directory.Exists(path));
    }

    /// <summary>Install folders of Epic games, from the launcher's per-game manifests.</summary>
    private static IEnumerable<string> FindEpicInstallLocations()
    {
        var manifests = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Epic", "EpicGamesLauncher", "Data", "Manifests");
        if (!Directory.Exists(manifests))
        {
            yield break;
        }

        foreach (var file in Directory.EnumerateFiles(manifests, "*.item"))
        {
            string? location = null;
            try
            {
                location = JsonNode.Parse(File.ReadAllText(file))?["InstallLocation"]?.GetValue<string>();
            }
            catch (Exception ex) when (ex is JsonException or IOException or InvalidOperationException)
            {
            }
            if (location.IsNotEmpty())
            {
                yield return location!;
            }
        }
    }
}
