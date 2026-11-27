using ServiceLib.Common;
using ServiceLib.Handler;

namespace ServiceLib.Tests.Common;

// Inside the namespace: this scope is searched first, so Should() binds to AwesomeAssertions, not to TUnit's global Should().
using AwesomeAssertions;

[NotInParallel(nameof(SvoRayPathsTests))]
public class SvoRayPathsTests
{
    [Test]
    public void IsLocalPath_ShouldAcceptFullyQualifiedPathsOnTheSystemDrive()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }
        var system = Path.GetPathRoot(Environment.SystemDirectory)!;

        SvoRayPaths.IsLocalPath(system).Should().BeTrue();
        SvoRayPaths.IsLocalPath(Path.Combine(system, "Games", "Steam")).Should().BeTrue();
        SvoRayPaths.IsLocalPath(system.Replace('\\', '/') + "Games/Steam").Should().BeTrue();
        SvoRayPaths.IsLocalPath(Path.GetTempPath()).Should().BeTrue();
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("Games")]
    [Arguments(@"..\Games")]
    [Arguments("C:Games")]
    [Arguments(@"\Games")]
    [Arguments(@"\\attacker\share\Games")]
    [Arguments("//attacker/share/Games")]
    [Arguments(@"\\?\C:\Games")]
    [Arguments(@"\\.\PhysicalDrive0")]
    [Arguments(@"\\?\UNC\attacker\share")]
    [Arguments("C:\\Games\u0000evil")]
    public void IsLocalPath_ShouldRefuseSharesDevicesAndRelativePaths(string? path)
    {
        SvoRayPaths.IsLocalPath(path).Should().BeFalse();
    }

    [Test]
    public void ExactCase_ShouldNotTouchAShareFromALauncherRecord()
    {
        SvoRayGames.ExactCase(@"\\attacker\share\Games").Should().BeNull();
        SvoRayGames.BuildFolderEntries([@"\\attacker\share\Games"]).Should().BeEmpty();
    }

    [Test]
    [Arguments("google")]
    [Arguments("geolocation-!cn")]
    [Arguments("category-ads-all")]
    [Arguments("google@cn")]
    public void IsPlainFileName_ShouldAcceptRuleSetNames(string name)
    {
        SvoRayPaths.IsPlainFileName(name).Should().BeTrue();
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("..")]
    [Arguments(@"google@..\..\..\evil")]
    [Arguments("google@../../evil")]
    [Arguments(@"a\b")]
    [Arguments("a/b")]
    [Arguments("C:evil")]
    [Arguments("evil\u0000")]
    public void IsPlainFileName_ShouldRefuseNamesThatLeaveTheFolder(string? name)
    {
        SvoRayPaths.IsPlainFileName(name).Should().BeFalse();
    }
}
