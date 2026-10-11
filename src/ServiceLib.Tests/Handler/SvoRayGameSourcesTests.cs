using System.Text;
using AwesomeAssertions;
using ServiceLib.Handler;
using Xunit;

namespace ServiceLib.Tests.Handler;

/// <summary>
/// Each launcher format on data shaped like the real files (no registry and no real launcher
/// installs are read). Layouts follow Playnite, GameFinder and the launchers' own files; see the
/// 0.5.0 release notes for the sources.
/// </summary>
public class SvoRayGameSourcesTests
{
    [Fact]
    public void ParseEpicManifest_ShouldReadTheInstallLocation()
    {
        SvoRayGames.ParseEpicManifest("""{ "DisplayName": "Fortnite", "InstallLocation": "D:\\Epic\\Fortnite" }""")
            .Should().Be(@"D:\Epic\Fortnite");
        SvoRayGames.ParseEpicManifest("{ broken").Should().BeNull();
        SvoRayGames.ParseEpicManifest(null).Should().BeNull();
    }

    [Fact]
    public void ParseRiotProductSettings_ShouldReadOnlyTheTopLevelInstallPath()
    {
        const string yaml = """
            locale_data:
                available_locales:
                - ru_RU
                product_install_full_path: "C:/ignored/nested"
            product_install_full_path: "E:/Games/Riot/VALORANT/live"
            product_install_root: "E:/Games/Riot"
            """;

        SvoRayGames.ParseRiotProductSettings(yaml).Should().Be("E:/Games/Riot/VALORANT/live");
        SvoRayGames.ParseRiotProductSettings("product_install_full_path: D:/LoL").Should().Be("D:/LoL");
        SvoRayGames.ParseRiotProductSettings("other: 1").Should().BeNull();
    }

    [Fact]
    public void ParseRiotClientInstalls_ShouldReadTheGameFoldersOfAssociatedClients()
    {
        const string json = """
            {
              "associated_client": { "D:/Riot Games/League of Legends/": "D:/Riot Games/Riot Client/RiotClientServices.exe" },
              "rc_default": "C:/Riot Games/Riot Client/RiotClientServices.exe"
            }
            """;

        SvoRayGames.ParseRiotClientInstalls(json).Should().Equal("D:/Riot Games/League of Legends/");
        SvoRayGames.ParseRiotClientInstalls("""{ "rc_live": "x" }""").Should().BeEmpty();
        SvoRayGames.ParseRiotClientInstalls("nonsense").Should().BeEmpty();
    }

    [Fact]
    public void ParseBattleNetProductDb_ShouldReadGamePathsAndSkipTheLauncher()
    {
        var db = Concat(
            Field(1, Concat(Field(1, Text("agent")), Field(3, Field(1, Text("C:/ProgramData/Battle.net/Agent"))))),
            Field(1, Concat(Field(1, Text("wow")), Field(2, Text("wow")), Field(3, Concat(Field(1, Text("E:/Games/World of Warcraft")), Field(2, Text("x")))))),
            Varint(2 << 3), Varint(150), // an unrelated varint field must be stepped over
            Field(1, Concat(Field(1, Text("pro")), Field(3, Field(1, Text("F:/Overwatch"))))));

        SvoRayGames.ParseBattleNetProductDb(db).Should().Equal("E:/Games/World of Warcraft", "F:/Overwatch");
    }

    [Fact]
    public void ParseBattleNetProductDb_ShouldSurviveTruncatedData()
    {
        var db = Field(1, Concat(Field(1, Text("wow")), Field(3, Field(1, Text("E:/Games/World of Warcraft")))));

        SvoRayGames.ParseBattleNetProductDb(db[..^5]).Should().BeEmpty();
        SvoRayGames.ParseBattleNetProductDb([0xFF, 0xFF, 0xFF]).Should().BeEmpty();
        SvoRayGames.ParseBattleNetProductDb(null).Should().BeEmpty();
    }

    [Fact]
    public void ParseGamingRoot_ShouldReadEveryRelativeFolder()
    {
        var data = Concat(Encoding.ASCII.GetBytes("RGBX"), BitConverter.GetBytes(2u),
            Encoding.Unicode.GetBytes("XboxGames\0"), Encoding.Unicode.GetBytes("Games\\Xbox\0"));

        SvoRayGames.ParseGamingRoot(data).Should().Equal("XboxGames", @"Games\Xbox");
        SvoRayGames.ParseGamingRoot(Encoding.ASCII.GetBytes("NOPE1234")).Should().BeEmpty();
        SvoRayGames.ParseGamingRoot([1, 2]).Should().BeEmpty();
    }

    [Fact]
    public void ParseLestaPreferences_ShouldReadEveryGameWorkingDir()
    {
        const string xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <protocol name="wgc_preferences">
              <application>
                <games_manager>
                  <games>
                    <game><working_dir>D:\Games\Tanki</working_dir></game>
                    <game><working_dir>E:\Lesta\Korabli</working_dir></game>
                  </games>
                </games_manager>
              </application>
            </protocol>
            """;

        SvoRayGames.ParseLestaPreferences(xml).Should().Equal(@"D:\Games\Tanki", @"E:\Lesta\Korabli");
        SvoRayGames.ParseLestaPreferences("<broken").Should().BeEmpty();
    }

    [Theory]
    [InlineData("Blizzard Entertainment", null, true)]
    [InlineData("Electronic Arts", null, true)]
    [InlineData("Electronic Arts, Inc.", null, true)]
    [InlineData("Rockstar Games", null, true)]
    [InlineData(null, "\"C:\\Program Files (x86)\\Battle.net\\Battle.net.exe\" --uid=wow --lang=ruRU", true)]
    [InlineData("Microsoft Corporation", "MsiExec.exe /X{...}", false)]
    [InlineData("Discord Inc.", null, false)]
    public void IsGameUninstallEntry_ShouldKeepOnlyGameLaunchers(string? publisher, string? uninstall, bool expected)
    {
        SvoRayGames.IsGameUninstallEntry(publisher, uninstall).Should().Be(expected);
    }

    [Fact]
    public void FindFileSystemFolders_ShouldReadEveryLauncherFromItsOwnFiles()
    {
        var root = Directory.CreateTempSubdirectory("svoray-games-").FullName;
        try
        {
            string Dir(params string[] parts) => Directory.CreateDirectory(Path.Combine([root, .. parts])).FullName;
            var data = Dir("ProgramData");
            var drive = Dir("DriveE");
            var epicGame = Dir("Library", "Fortnite");
            var riotGame = Dir("Library", "VALORANT");
            var bnetGame = Dir("Library", "WoW");
            var lestaGame = Dir("Library", "Tanki");
            var xboxGames = Dir("DriveE", "XboxGames");
            var roblox = Dir("Local", "Roblox", "Versions");
            var battlEye = Dir("Common", "BattlEye");

            File.WriteAllText(Path.Combine(Dir("ProgramData", "Epic", "EpicGamesLauncher", "Data", "Manifests"), "a.item"),
                $$"""{ "InstallLocation": {{System.Text.Json.JsonSerializer.Serialize(epicGame)}} }""");
            File.WriteAllText(Path.Combine(Dir("ProgramData", "Riot Games", "Metadata", "valorant.live"), "valorant.live.product_settings.yaml"),
                $"product_install_full_path: \"{riotGame.Replace('\\', '/')}\"\n");
            File.WriteAllBytes(Path.Combine(Dir("ProgramData", "Battle.net", "Agent"), "product.db"),
                Field(1, Concat(Field(1, Text("wow")), Field(3, Field(1, Text(bnetGame.Replace('\\', '/')))))));
            File.WriteAllText(Path.Combine(Dir("ProgramData", "Lesta", "GameCenter"), "preferences.xml"),
                $"<p><application><games_manager><games><game><working_dir>{lestaGame}</working_dir></game></games></games_manager></application></p>");
            File.WriteAllBytes(Path.Combine(drive, ".GamingRoot"),
                Concat(Encoding.ASCII.GetBytes("RGBX"), BitConverter.GetBytes(1u), Encoding.Unicode.GetBytes("XboxGames\0")));
            // A broken manifest must only cost its own source nothing at all.
            File.WriteAllText(Path.Combine(root, "ProgramData", "Epic", "EpicGamesLauncher", "Data", "Manifests", "b.item"), "{ broken");

            var at = new SvoRayGameLocations
            {
                ProgramData = data,
                LocalAppData = Path.Combine(root, "Local"),
                ProgramFiles = Path.Combine(root, "PF"),
                ProgramFilesX86 = Path.Combine(root, "PF86"),
                CommonFilesX86 = Path.Combine(root, "Common"),
                SystemDrive = Path.Combine(root, "DriveC"),
                FixedDrives = [drive],
            };

            var entries = SvoRayGames.BuildFolderEntries(SvoRayGames.FindFileSystemFolders(at));

            string Entry(string path) => SvoRayGames.ExactCase(path)!.Replace('\\', '/') + "/";
            entries.Should().BeEquivalentTo(
                [Entry(epicGame), Entry(riotGame), Entry(bnetGame), Entry(lestaGame), Entry(xboxGames), Entry(roblox), Entry(battlEye)]);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void IsTooBroad_ShouldRefuseSystemFoldersAndStoreApps()
    {
        SvoRayGames.IsTooBroad(@"C:\").Should().BeTrue();
        SvoRayGames.IsTooBroad(Environment.GetFolderPath(Environment.SpecialFolder.Windows)).Should().BeTrue();
        SvoRayGames.IsTooBroad(Environment.GetFolderPath(Environment.SpecialFolder.System)).Should().BeTrue();
        SvoRayGames.IsTooBroad(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)).Should().BeTrue();
        SvoRayGames.IsTooBroad(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) + @"\").Should().BeTrue();
        SvoRayGames.IsTooBroad(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)).Should().BeTrue();
        SvoRayGames.IsTooBroad(@"C:\Program Files\WindowsApps\Some.Game_1.0").Should().BeTrue();
        SvoRayGames.IsTooBroad(null).Should().BeTrue();

        SvoRayGames.IsTooBroad(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Epic Games")).Should().BeFalse();
        SvoRayGames.IsTooBroad(@"E:\SteamLibrary\steamapps\common").Should().BeFalse();
    }

    [Fact]
    public void BuildFolderEntries_ShouldDropAnInstallLocationThatIsTooBroad()
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

        SvoRayGames.BuildFolderEntries([programFiles, Path.GetPathRoot(programFiles)!]).Should().BeEmpty();
    }

    [Fact]
    public void CheckCustomFolder_ShouldExplainWhyAFolderIsRefused()
    {
        var game = Directory.CreateTempSubdirectory("svoray-custom-").FullName;
        try
        {
            SvoRayGames.CheckCustomFolder(game).Should().Be(ESvoRayFolderCheck.Ok);
            SvoRayGames.CheckCustomFolder(Path.Combine(game, "missing")).Should().Be(ESvoRayFolderCheck.Missing);
            SvoRayGames.CheckCustomFolder(Environment.GetFolderPath(Environment.SpecialFolder.Windows)).Should().Be(ESvoRayFolderCheck.TooBroad);
            SvoRayGames.CheckCustomFolder(AppContext.BaseDirectory).Should().Be(ESvoRayFolderCheck.SvoRay);
        }
        finally
        {
            Directory.Delete(game, true);
        }
    }

    [Fact]
    public void ExactCase_ShouldResolveAJunctionToItsTarget()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var root = Directory.CreateTempSubdirectory("svoray-junction-").FullName;
        try
        {
            var target = Directory.CreateDirectory(Path.Combine(root, "RealLibrary")).FullName;
            var link = Path.Combine(root, "link");
            // A junction needs no administrator rights, unlike a directory symlink.
            using (var mklink = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
            })!)
            {
                mklink.WaitForExit();
            }
            if (!Directory.Exists(link))
            {
                return;
            }

            SvoRayGames.ExactCase(link).Should().Be(SvoRayGames.ExactCase(target));
            SvoRayGames.ExactCase(link)!.Should().EndWith("RealLibrary");
        }
        finally
        {
            var link = Path.Combine(root, "link");
            if (Directory.Exists(link))
            {
                Directory.Delete(link);
            }
            Directory.Delete(root, true);
        }
    }

    private static byte[] Text(string value) => Encoding.UTF8.GetBytes(value);

    private static byte[] Field(int number, byte[] payload) => Concat(Varint((ulong)((number << 3) | 2)), Varint((ulong)payload.Length), payload);

    private static byte[] Varint(ulong value)
    {
        var bytes = new List<byte>();
        do
        {
            var b = (byte)(value & 0x7F);
            value >>= 7;
            bytes.Add(value != 0 ? (byte)(b | 0x80) : b);
        }
        while (value != 0);
        return [.. bytes];
    }

    private static byte[] Concat(params byte[][] parts) => parts.SelectMany(p => p).ToArray();
}
