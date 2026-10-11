using AwesomeAssertions;
using ServiceLib.Handler;
using Xunit;

namespace ServiceLib.Tests.Handler;

public class SvoRayGamesTests
{
    [Fact]
    public void ParseLibraryFolders_ShouldReadEveryLibraryPath()
    {
        const string vdf = """
            "libraryfolders"
            {
                "0"
                {
                    "path"		"c:\\program files (x86)\\steam"
                    "label"		""
                    "apps" { "730" "36451749184" }
                }
                "1"
                {
                    "path"		"F:\\SteamLibrary"
                }
            }
            """;

        SvoRayGames.ParseLibraryFolders(vdf).Should()
            .Equal(@"c:\program files (x86)\steam", @"F:\SteamLibrary");
        SvoRayGames.ParseLibraryFolders(null).Should().BeEmpty();
    }

    /// <summary>
    /// Xray compares folders case-sensitively against the path Windows reports, while Steam keeps
    /// library paths lower-cased. The entry has to come from the directory names on disk.
    /// </summary>
    [Fact]
    public void ExactCase_ShouldSpellThePathAsTheFileSystemDoes()
    {
        var root = Directory.CreateTempSubdirectory();
        try
        {
            var real = Directory.CreateDirectory(Path.Combine(root.FullName, "SteamLibrary", "steamapps", "Common"));
            var typed = Path.Combine(root.FullName, "steamlibrary", "STEAMAPPS", "common");

            var exact = SvoRayGames.ExactCase(typed);

            exact.Should().NotBeNull();
            exact!.EndsWith(Path.Combine("SteamLibrary", "steamapps", "Common"), StringComparison.Ordinal)
                .Should().BeTrue($"got {exact}");
            Directory.Exists(exact).Should().BeTrue();
            real.Exists.Should().BeTrue();
        }
        finally
        {
            root.Delete(true);
        }
    }

    [Fact]
    public void ExactCase_ShouldReturnNullForAMissingFolder()
    {
        SvoRayGames.ExactCase(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))).Should().BeNull();
        SvoRayGames.ExactCase(null).Should().BeNull();
    }

    [Fact]
    public void BuildFolderEntries_ShouldKeepExistingFoldersOnceAndDropNestedOnes()
    {
        var root = Directory.CreateTempSubdirectory();
        try
        {
            var steam = Directory.CreateDirectory(Path.Combine(root.FullName, "Steam")).FullName;
            var common = Directory.CreateDirectory(Path.Combine(steam, "steamapps", "common")).FullName;
            var other = Directory.CreateDirectory(Path.Combine(root.FullName, "Games")).FullName;

            var entries = SvoRayGames.BuildFolderEntries(
                [common, steam, steam.ToUpperInvariant(), other, Path.Combine(root.FullName, "Missing")]);

            entries.Should().HaveCount(2);
            entries.Should().OnlyContain(e => e.EndsWith('/') && !e.Contains('\\'));
            entries.Should().Contain(e => e.EndsWith("/Steam/", StringComparison.Ordinal));
            entries.Should().Contain(e => e.EndsWith("/Games/", StringComparison.Ordinal));
        }
        finally
        {
            root.Delete(true);
        }
    }

    [Fact]
    public void ProcessNames_ShouldCoverTheMainLaunchers()
    {
        SvoRayGames.ProcessNames.Should().Contain(["steam.exe", "steamwebhelper.exe", "EpicGamesLauncher.exe"]);
        SvoRayGames.ProcessNames.Should().NotContain(n => n.Contains("discord", StringComparison.OrdinalIgnoreCase));
    }
}
