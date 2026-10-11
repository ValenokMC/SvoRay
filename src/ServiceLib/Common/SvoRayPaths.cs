namespace ServiceLib.Common;

/// <summary>
/// Checks for paths SvoRay reads from data it does not control: launcher records, the registry,
/// a folder the user picked, its own data folder.
/// </summary>
/// <remarks>
/// SvoRay runs elevated, and some launcher records live in folders any user can write to
/// (ProgramData). A planted <c>\\host\share</c> path would make it open a network share, and
/// Windows would hand that host the user's NTLM credentials. Only fully qualified paths on a
/// local drive are touched.
/// </remarks>
public static class SvoRayPaths
{
    /// <summary>
    /// True for a fully qualified path on a local fixed, removable or RAM drive, such as
    /// <c>C:\Games</c> or <c>E:/SteamLibrary</c>. False for shares (<c>\\host\share</c>), device
    /// paths (<c>\\?\</c>, <c>\\.\</c>), relative paths, mapped network drives and anything
    /// malformed.
    /// </summary>
    public static bool IsLocalPath(string? path)
    {
        if (path is null || path.Length < 3 || path.Length > 32767)
        {
            return false;
        }
        if (!char.IsAsciiLetter(path[0]) || path[1] != ':' || (path[2] != '\\' && path[2] != '/'))
        {
            return false;
        }
        if (path.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
        {
            return false;
        }

        try
        {
            if (!Path.IsPathFullyQualified(path))
            {
                return false;
            }
            var drive = new DriveInfo(path[..1]);
            return drive.DriveType is DriveType.Fixed or DriveType.Removable or DriveType.Ram;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
