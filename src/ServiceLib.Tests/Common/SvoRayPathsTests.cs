using AwesomeAssertions;
using ServiceLib.Common;
using ServiceLib.Handler;
using Xunit;

namespace ServiceLib.Tests.Common;

public class SvoRayPathsTests
{
    [Fact]
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

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Games")]
    [InlineData(@"..\Games")]
    [InlineData("C:Games")]
    [InlineData(@"\Games")]
    [InlineData(@"\\attacker\share\Games")]
    [InlineData("//attacker/share/Games")]
    [InlineData(@"\\?\C:\Games")]
    [InlineData(@"\\.\PhysicalDrive0")]
    [InlineData(@"\\?\UNC\attacker\share")]
    [InlineData("C:\\Games\u0000evil")]
    public void IsLocalPath_ShouldRefuseSharesDevicesAndRelativePaths(string? path)
    {
        SvoRayPaths.IsLocalPath(path).Should().BeFalse();
    }

    [Fact]
    public void ExactCase_ShouldNotTouchAShareFromALauncherRecord()
    {
        SvoRayGames.ExactCase(@"\\attacker\share\Games").Should().BeNull();
        SvoRayGames.BuildFolderEntries([@"\\attacker\share\Games"]).Should().BeEmpty();
    }
}
