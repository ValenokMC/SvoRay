using System.Xml.Linq;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace ServiceLib.Handler;

/// <summary>Why a folder the user picked is or is not used by game mode.</summary>
public enum ESvoRayFolderCheck
{
    Ok,

    /// <summary>The folder does not exist (any more).</summary>
    Missing,

    /// <summary>
    /// A drive root, Windows, a whole Program Files, the user profile and the like: far more
    /// than games would leave the VPN.
    /// </summary>
    TooBroad,

    /// <summary>SvoRay's own program or data folder, or a folder that contains one of them.</summary>
    SvoRay,
}

/// <summary>
/// The system folders the game sources read from. A record so tests can point every source at
/// a scratch folder instead of the real machine.
/// </summary>
public sealed record SvoRayGameLocations
{
    public string ProgramData { get; init; } = "";
    public string LocalAppData { get; init; } = "";
    public string ProgramFiles { get; init; } = "";
    public string ProgramFilesX86 { get; init; } = "";
    public string CommonFilesX86 { get; init; } = "";
    public string SystemDrive { get; init; } = "";

    /// <summary>Roots of the fixed drives, such as <c>C:\</c>; Xbox can keep games on each.</summary>
    public IReadOnlyList<string> FixedDrives { get; init; } = [];

    public static SvoRayGameLocations Current()
    {
        return new SvoRayGameLocations
        {
            ProgramData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            LocalAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            ProgramFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            ProgramFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            CommonFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFilesX86),
            SystemDrive = Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\",
            FixedDrives = FixedDriveRoots(),
        };
    }

    private static List<string> FixedDriveRoots()
    {
        try
        {
            return DriveInfo.GetDrives()
                .Where(d => d.DriveType == DriveType.Fixed && d.IsReady)
                .Select(d => d.RootDirectory.FullName)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}

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
/// ⚠️ Xray compares case-sensitively against the path Windows reports for the process
/// (QueryFullProcessImageName), which is the final path on disk: real letter case, junctions and
/// symbolic links resolved. Every folder is therefore turned into its final path first; Steam,
/// for one, stores library paths lower-cased, and a rule built from that text would never fire.
/// </para>
/// <para>
/// Every source is read on its own and a failure only drops that source: a broken launcher
/// install must never stop the VPN from connecting. Logs name the source and the error type,
/// never a path - folder names say what someone plays.
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

    private const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

    /// <summary>
    /// Folder entries for the routing rule, each ending in <c>/</c> and spelled as Windows reports
    /// process paths. Empty on systems other than Windows.
    /// </summary>
    public static List<string> FindGameFolders(IEnumerable<string>? customFolders = null)
    {
        if (!OperatingSystem.IsWindows())
        {
            return [];
        }

        var locations = SvoRayGameLocations.Current();
        var candidates = FindFileSystemFolders(locations);
        Collect("steam", candidates, FindSteamFolders);
        Collect("gog", candidates, FindGogFolders);
        Collect("ubisoft", candidates, FindUbisoftFolders);
        Collect("hoyoplay", candidates, FindHoYoPlayFolders);
        Collect("uninstall", candidates, FindUninstallFolders);

        // Folders the user added by hand, for games no launcher reports.
        foreach (var folder in customFolders ?? [])
        {
            if (CheckCustomFolder(folder) == ESvoRayFolderCheck.Ok)
            {
                candidates.Add(folder);
            }
        }

        return BuildFolderEntries(candidates);
    }

    /// <summary>
    /// Everything that comes from files and folders rather than the registry: the default install
    /// roots of the launchers, their own records of where games went, Xbox game roots on every
    /// drive, Roblox, and the anti-cheat services that run outside game folders.
    /// </summary>
    public static List<string> FindFileSystemFolders(SvoRayGameLocations at)
    {
        var candidates = new List<string>();
        Collect("known roots", candidates, () => KnownRoots(at));
        Collect("epic", candidates, () => FindEpicFolders(at));
        Collect("riot", candidates, () => FindRiotFolders(at));
        Collect("battle.net", candidates, () => FindBattleNetFolders(at));
        Collect("lesta", candidates, () => FindLestaFolders(at));
        Collect("xbox", candidates, () => FindXboxFolders(at));
        return candidates;
    }

    /// <summary>
    /// Default install roots, plus services that live apart from the games they protect. Each one
    /// was checked against the vendor's own documentation or open-source launcher code (Playnite,
    /// GameFinder); see docs/RELEASE_NOTES_0.5.0_RU.md.
    /// </summary>
    private static IEnumerable<string> KnownRoots(SvoRayGameLocations at)
    {
        return
        [
            Path.Combine(at.ProgramFiles, "Epic Games"),
            Path.Combine(at.ProgramFilesX86, "Epic Games"),
            Path.Combine(at.ProgramFiles, "EA Games"),
            Path.Combine(at.ProgramFiles, "Electronic Arts"),
            Path.Combine(at.ProgramFilesX86, "Origin Games"),
            Path.Combine(at.ProgramFilesX86, "Battle.net"),
            Path.Combine(at.ProgramFilesX86, "Ubisoft", "Ubisoft Game Launcher"),
            Path.Combine(at.ProgramFilesX86, "GOG Galaxy", "Games"),
            Path.Combine(at.SystemDrive, "Riot Games"),
            Path.Combine(at.SystemDrive, "XboxGames"),
            // Roblox installs per user, a new "version-..." folder for every update.
            Path.Combine(at.LocalAppData, "Roblox", "Versions"),
            Path.Combine(at.ProgramFilesX86, "Roblox", "Versions"),
            // Anti-cheat services start outside the game folder and talk to their own servers.
            Path.Combine(at.ProgramFilesX86, "EasyAntiCheat_EOS"),
            Path.Combine(at.ProgramFilesX86, "EasyAntiCheat"),
            Path.Combine(at.CommonFilesX86, "BattlEye"),
        ];
    }

    #region Formats

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

    /// <summary><c>InstallLocation</c> of an Epic Games Launcher manifest (<c>*.item</c>, JSON).</summary>
    public static string? ParseEpicManifest(string? json)
    {
        if (json.IsNullOrEmpty())
        {
            return null;
        }
        try
        {
            return JsonNode.Parse(json!)?["InstallLocation"]?.GetValue<string>();
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    /// <summary>
    /// <c>product_install_full_path</c> of a Riot <c>*.product_settings.yaml</c>: a top-level
    /// <c>key: value</c> line, quoted or not, with forward slashes. Indented lines belong to
    /// nested maps and are ignored, so no YAML library is needed.
    /// </summary>
    public static string? ParseRiotProductSettings(string? yaml)
    {
        foreach (var line in (yaml ?? "").Split('\n'))
        {
            if (line.Length == 0 || char.IsWhiteSpace(line[0]) || !line.StartsWith("product_install_full_path:", StringComparison.Ordinal))
            {
                continue;
            }
            var value = line["product_install_full_path:".Length..].Trim().Trim('"', '\'');
            return value.IsNotEmpty() ? value : null;
        }
        return null;
    }

    /// <summary>
    /// Game folders from Riot's <c>RiotClientInstalls.json</c>: the keys of
    /// <c>associated_client</c>, which map each installed game to its Riot Client.
    /// </summary>
    public static List<string> ParseRiotClientInstalls(string? json)
    {
        if (json.IsNullOrEmpty())
        {
            return [];
        }
        try
        {
            return JsonNode.Parse(json!)?["associated_client"] is JsonObject map
                ? map.Select(pair => pair.Key).Where(key => key.IsNotEmpty()).ToList()
                : [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>
    /// Install paths from Battle.net's <c>product.db</c>, a protobuf message: repeated field 1
    /// is a product with its uid in field 1 and install data in field 3, whose field 1 is the
    /// path. The launcher's own components ("agent", "battle.net", "bna") are skipped. Battle.net
    /// does not write uninstall entries for every game, which is why this file is read at all.
    /// </summary>
    public static List<string> ParseBattleNetProductDb(byte[]? data)
    {
        var result = new List<string>();
        if (data is null)
        {
            return result;
        }

        foreach (var product in ReadField(data, 1))
        {
            var uid = ReadField(product, 1).Select(b => Encoding.UTF8.GetString(b)).FirstOrDefault();
            if (uid is null || uid.Equals("agent", StringComparison.OrdinalIgnoreCase)
                || uid.Equals("battle.net", StringComparison.OrdinalIgnoreCase)
                || uid.Equals("bna", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var path = ReadField(product, 3).SelectMany(install => ReadField(install, 1))
                .Select(b => Encoding.UTF8.GetString(b))
                .FirstOrDefault(p => p.IsNotEmpty());
            if (path is not null)
            {
                result.Add(path);
            }
        }
        return result;
    }

    /// <summary>
    /// Game roots from an Xbox app <c>.GamingRoot</c> file at a drive root: the bytes "RGBX", a
    /// 32-bit count, then that many NUL-terminated UTF-16 paths relative to the drive.
    /// </summary>
    public static List<string> ParseGamingRoot(byte[]? data)
    {
        var result = new List<string>();
        if (data is null || data.Length < 8 || Encoding.ASCII.GetString(data, 0, 4) != "RGBX")
        {
            return result;
        }

        var count = BitConverter.ToUInt32(data, 4);
        var position = 8;
        for (var i = 0; i < count && position + 1 < data.Length; i++)
        {
            var start = position;
            while (position + 1 < data.Length && (data[position] != 0 || data[position + 1] != 0))
            {
                position += 2;
            }
            var text = Encoding.Unicode.GetString(data, start, position - start);
            position += 2;
            if (text.IsNotEmpty())
            {
                result.Add(text);
            }
        }
        return result;
    }

    /// <summary>
    /// Game folders from the Lesta (or Wargaming) Game Center <c>preferences.xml</c>: every
    /// <c>application/games_manager/games/game/working_dir</c>.
    /// </summary>
    public static List<string> ParseLestaPreferences(string? xml)
    {
        if (xml.IsNullOrEmpty())
        {
            return [];
        }
        try
        {
            return XDocument.Parse(xml!)
                .Descendants("games_manager").Elements("games").Elements("game").Elements("working_dir")
                .Select(e => e.Value.Trim())
                .Where(v => v.IsNotEmpty())
                .ToList();
        }
        catch (System.Xml.XmlException)
        {
            return [];
        }
    }

    /// <summary>
    /// Whether an uninstall entry belongs to a game launcher whose games can sit anywhere:
    /// Blizzard games installed through Battle.net, EA app games and Rockstar games.
    /// </summary>
    public static bool IsGameUninstallEntry(string? publisher, string? uninstallString)
    {
        publisher = publisher?.Trim();
        return string.Equals(publisher, "Blizzard Entertainment", StringComparison.OrdinalIgnoreCase)
            || string.Equals(publisher, "Rockstar Games", StringComparison.OrdinalIgnoreCase)
            || publisher?.Contains("Electronic Arts", StringComparison.OrdinalIgnoreCase) == true
            || (uninstallString?.Contains("Battle.net", StringComparison.OrdinalIgnoreCase) == true
                && uninstallString.Contains("--uid=", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The length-delimited values of one field number in a protobuf message.</summary>
    private static IEnumerable<byte[]> ReadField(byte[] message, int wanted)
    {
        var position = 0;
        while (position < message.Length)
        {
            if (!TryReadVarint(message, ref position, out var key))
            {
                yield break;
            }

            var field = (int)(key >> 3);
            switch (key & 7)
            {
                case 0:
                    if (!TryReadVarint(message, ref position, out _))
                    {
                        yield break;
                    }
                    break;

                case 1:
                    position += 8;
                    break;

                case 2:
                    if (!TryReadVarint(message, ref position, out var length) || length > (ulong)(message.Length - position))
                    {
                        yield break;
                    }
                    if (field == wanted)
                    {
                        yield return message[position..(position + (int)length)];
                    }
                    position += (int)length;
                    break;

                case 5:
                    position += 4;
                    break;

                default:
                    yield break;
            }
        }
    }

    private static bool TryReadVarint(byte[] data, ref int position, out ulong value)
    {
        value = 0;
        for (var shift = 0; shift < 64 && position < data.Length; shift += 7)
        {
            var b = data[position++];
            value |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0)
            {
                return true;
            }
        }
        return false;
    }

    #endregion Formats

    #region Sources

    private static void Collect(string source, List<string> into, Func<IEnumerable<string>> read)
    {
        try
        {
            into.AddRange(read().Where(path => path.IsNotEmpty()));
        }
        catch (Exception ex)
        {
            Logging.SaveLog($"SvoRay game source '{source}' skipped: {ex.GetType().Name}");
        }
    }

    [SupportedOSPlatform("windows")]
    private static IEnumerable<string> FindSteamFolders()
    {
        var steam = new[]
        {
            Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string,
            Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", null) as string,
            Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Valve\Steam", "InstallPath", null) as string,
        }.FirstOrDefault(path => SvoRayPaths.IsLocalPath(path) && Directory.Exists(path));
        if (steam is null)
        {
            return [];
        }

        // Steam itself: the client, its web helper and the default library under it.
        var result = new List<string> { steam };
        var vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
        if (SvoRayPaths.IsLocalPath(vdf) && File.Exists(vdf))
        {
            result.AddRange(ParseLibraryFolders(File.ReadAllText(vdf)).Select(lib => Path.Combine(lib, "steamapps", "common")));
        }
        return result;
    }

    private static IEnumerable<string> FindEpicFolders(SvoRayGameLocations at)
    {
        var manifests = Path.Combine(at.ProgramData, "Epic", "EpicGamesLauncher", "Data", "Manifests");
        return SvoRayPaths.IsLocalPath(manifests) && Directory.Exists(manifests)
            ? Directory.EnumerateFiles(manifests, "*.item").Select(f => ParseEpicManifest(SafeReadText(f))).OfType<string>().ToList()
            : [];
    }

    private static IEnumerable<string> FindRiotFolders(SvoRayGameLocations at)
    {
        var result = new List<string>();
        var riot = Path.Combine(at.ProgramData, "Riot Games");
        var metadata = Path.Combine(riot, "Metadata");
        if (SvoRayPaths.IsLocalPath(metadata) && Directory.Exists(metadata))
        {
            result.AddRange(Directory.EnumerateFiles(metadata, "*.product_settings.yaml", SearchOption.AllDirectories)
                .Where(f => !Path.GetFileName(f).StartsWith("riot_client", StringComparison.OrdinalIgnoreCase))
                .Select(f => ParseRiotProductSettings(SafeReadText(f)))
                .OfType<string>());
        }
        result.AddRange(ParseRiotClientInstalls(SafeReadText(Path.Combine(riot, "RiotClientInstalls.json"))));
        return result;
    }

    private static IEnumerable<string> FindBattleNetFolders(SvoRayGameLocations at)
    {
        var db = Path.Combine(at.ProgramData, "Battle.net", "Agent", "product.db");
        return SvoRayPaths.IsLocalPath(db) && File.Exists(db) ? ParseBattleNetProductDb(File.ReadAllBytes(db)) : [];
    }

    private static IEnumerable<string> FindLestaFolders(SvoRayGameLocations at)
    {
        return new[]
            {
                Path.Combine(at.ProgramData, "Lesta", "GameCenter", "preferences.xml"),
                Path.Combine(at.ProgramData, "Wargaming.net", "GameCenter", "preferences.xml"),
            }
            .SelectMany(f => ParseLestaPreferences(SafeReadText(f)))
            .ToList();
    }

    /// <summary>
    /// Folders the Xbox app installs PC games into: one per drive, listed in that drive's
    /// <c>.GamingRoot</c>. Never <c>WindowsApps</c> - every Store app lives there, not just games.
    /// </summary>
    private static IEnumerable<string> FindXboxFolders(SvoRayGameLocations at)
    {
        var result = new List<string>();
        foreach (var drive in at.FixedDrives)
        {
            var file = Path.Combine(drive, ".GamingRoot");
            if (SvoRayPaths.IsLocalPath(file) && File.Exists(file))
            {
                result.AddRange(ParseGamingRoot(File.ReadAllBytes(file)).Select(relative => Path.Combine(drive, relative.TrimStart('\\', '/'))));
            }
        }
        return result;
    }

    [SupportedOSPlatform("windows")]
    private static IEnumerable<string> FindGogFolders()
    {
        return SubkeyValues(RegistryHive.LocalMachine, RegistryView.Registry32, @"SOFTWARE\GOG.com\Games", "path");
    }

    [SupportedOSPlatform("windows")]
    private static IEnumerable<string> FindUbisoftFolders()
    {
        // Ubisoft writes forward slashes, often with one at the end.
        return SubkeyValues(RegistryHive.LocalMachine, RegistryView.Registry32, @"SOFTWARE\Ubisoft\Launcher\Installs", "InstallDir")
            .Concat(SubkeyValues(RegistryHive.LocalMachine, RegistryView.Registry64, @"SOFTWARE\Ubisoft\Launcher\Installs", "InstallDir"))
            .Select(path => path.Replace('/', '\\').TrimEnd('\\'))
            .ToList();
    }

    [SupportedOSPlatform("windows")]
    private static IEnumerable<string> FindHoYoPlayFolders()
    {
        return SubkeyValues(RegistryHive.CurrentUser, RegistryView.Default, @"SOFTWARE\Cognosphere\HYP\1_0", "GameInstallPath")
            .Concat(SubkeyValues(RegistryHive.CurrentUser, RegistryView.Default, @"SOFTWARE\miHoYo\HYP\1_1", "GameInstallPath"))
            .ToList();
    }

    /// <summary>
    /// Install folders from the uninstall entries of launchers that put games anywhere (both
    /// registry views, both hives - some games register only under one of them).
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static IEnumerable<string> FindUninstallFolders()
    {
        var result = new List<string>();
        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        {
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                using var root = RegistryKey.OpenBaseKey(hive, view).OpenSubKey(UninstallKey);
                if (root is null)
                {
                    continue;
                }
                foreach (var name in root.GetSubKeyNames())
                {
                    using var entry = root.OpenSubKey(name);
                    var location = (entry?.GetValue("InstallLocation") as string)?.Trim().Trim('"');
                    if (location.IsNotEmpty()
                        && IsGameUninstallEntry(entry!.GetValue("Publisher") as string, entry.GetValue("UninstallString") as string))
                    {
                        result.Add(location!);
                    }
                }
            }
        }
        return result;
    }

    [SupportedOSPlatform("windows")]
    private static List<string> SubkeyValues(RegistryHive hive, RegistryView view, string path, string valueName)
    {
        using var root = RegistryKey.OpenBaseKey(hive, view).OpenSubKey(path);
        if (root is null)
        {
            return [];
        }
        var result = new List<string>();
        foreach (var name in root.GetSubKeyNames())
        {
            using var key = root.OpenSubKey(name);
            if (key?.GetValue(valueName) is string value && value.IsNotEmpty())
            {
                result.Add(value.Trim().Trim('"'));
            }
        }
        return result;
    }

    private static string? SafeReadText(string path)
    {
        try
        {
            return SvoRayPaths.IsLocalPath(path) && File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    #endregion Sources

    #region Folders

    /// <summary>
    /// Turns candidate paths into rule entries: only folders that exist and are not too broad, as
    /// their final path on disk, with forward slashes and a trailing slash, without duplicates or
    /// folders already covered by a parent in the list.
    /// </summary>
    public static List<string> BuildFolderEntries(IEnumerable<string> candidates)
    {
        var entries = candidates
            .Select(ExactCase)
            .Where(path => path is not null && !IsTooBroad(path))
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
    /// Whether a folder the user picked can go into game mode. It must exist, be narrower than a
    /// system folder, and have nothing to do with SvoRay itself.
    /// </summary>
    public static ESvoRayFolderCheck CheckCustomFolder(string? path)
    {
        var final = ExactCase(path);
        if (final is null)
        {
            return ESvoRayFolderCheck.Missing;
        }
        if (IsTooBroad(final))
        {
            return ESvoRayFolderCheck.TooBroad;
        }

        var own = new[] { Utils.GetBaseDirectory(), Utils.StartupPath() }
            .Select(ExactCase)
            .OfType<string>();
        return own.Any(dir => IsSameOrInside(final, dir) || IsSameOrInside(dir, final))
            ? ESvoRayFolderCheck.SvoRay
            : ESvoRayFolderCheck.Ok;
    }

    /// <summary>
    /// A folder whose processes are far more than games: a drive root, anything inside Windows,
    /// Program Files or ProgramData as a whole, the user profile and its AppData roots, and Store
    /// apps (<c>WindowsApps</c>). Applied to every source, not only to the user's own folders: a
    /// launcher's uninstall entry can carry an install location as wide as "C:\Program Files".
    /// </summary>
    public static bool IsTooBroad(string? path)
    {
        if (path.IsNullOrEmpty())
        {
            return true;
        }

        string full;
        try
        {
            full = Path.GetFullPath(path!).TrimEnd('\\', '/');
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return true;
        }

        var root = (Path.GetPathRoot(full) ?? "").TrimEnd('\\', '/');
        if (full.Length <= root.Length)
        {
            return true;
        }
        if (full.Split('\\', '/').Any(part => part.Equals("WindowsApps", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (windows.IsNotEmpty() && IsSameOrInside(full, windows))
        {
            return true;
        }

        var exact = new[]
        {
            Environment.SpecialFolder.ProgramFiles,
            Environment.SpecialFolder.ProgramFilesX86,
            Environment.SpecialFolder.CommonProgramFiles,
            Environment.SpecialFolder.CommonProgramFilesX86,
            Environment.SpecialFolder.CommonApplicationData,
            Environment.SpecialFolder.UserProfile,
            Environment.SpecialFolder.ApplicationData,
            Environment.SpecialFolder.LocalApplicationData,
            Environment.SpecialFolder.Desktop,
            Environment.SpecialFolder.MyDocuments,
        }.Select(Environment.GetFolderPath).Where(p => p.IsNotEmpty());
        if (exact.Any(p => string.Equals(full, p.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        // The parent of a user profile ("C:\Users") holds every user's programs.
        var profiles = Path.GetDirectoryName(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        return profiles.IsNotEmpty() && string.Equals(full, profiles!.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSameOrInside(string path, string folder)
    {
        var p = path.TrimEnd('\\', '/') + "\\";
        var f = folder.TrimEnd('\\', '/') + "\\";
        return p.StartsWith(f, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The path as Windows reports a process started from it, or null when it does not exist:
    /// the final path (letter case from the disk, junctions and symbolic links resolved), which
    /// is what QueryFullProcessImageName returns. Falls back to rebuilding the letter case from
    /// directory listings where the final path cannot be read.
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
            // Launcher records and the registry are not trusted: never touch a share or a device.
            if (!SvoRayPaths.IsLocalPath(full) || !Directory.Exists(full))
            {
                return null;
            }

            if (OperatingSystem.IsWindows() && FinalPath(full) is { } final)
            {
                // A symbolic link on a local drive can still lead to a share.
                return SvoRayPaths.IsLocalPath(final) ? final : null;
            }
            return WalkCase(full);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
                                       or NotSupportedException)
        {
            return null;
        }
    }

    private static string? WalkCase(string full)
    {
        var root = Path.GetPathRoot(full)!;
        var current = root.Length >= 2 && root[1] == ':'
            ? char.ToUpperInvariant(root[0]) + root[1..]
            : root;

        var parts = full[root.Length..].Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            if (!SvoRayPaths.IsLocalPath(current))
            {
                return null;
            }
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

    [SupportedOSPlatform("windows")]
    private static string? FinalPath(string folder)
    {
        const uint FileReadAttributes = 0x80;
        const uint ShareAll = 0x7;
        const uint OpenExisting = 3;
        const uint BackupSemantics = 0x02000000;

        if (!SvoRayPaths.IsLocalPath(folder))
        {
            return null;
        }

        using var handle = CreateFileW(folder, FileReadAttributes, ShareAll, IntPtr.Zero, OpenExisting, BackupSemantics, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            return null;
        }

        var buffer = new char[1024];
        var length = GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Length, 0);
        if (length == 0 || length >= buffer.Length)
        {
            return null;
        }

        var text = new string(buffer, 0, (int)length);
        if (text.StartsWith(@"\\?\UNC\", StringComparison.Ordinal))
        {
            return @"\\" + text[8..];
        }
        return text.StartsWith(@"\\?\", StringComparison.Ordinal) ? text[4..] : text;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security,
        uint disposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandleW(SafeFileHandle handle, [Out] char[] buffer, uint length, uint flags);

    #endregion Folders
}
