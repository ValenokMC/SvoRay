namespace ServiceLib.Common;

/// <summary>
/// Traffic used and allowed, from the <c>Subscription-Userinfo</c> header most panels send:
/// <c>upload=1; download=2; total=3; expire=0</c>. Shown on the simple screen so a quota is
/// visible before the server cuts the connection, not after.
/// </summary>
public sealed record SvoRaySubscriptionUsage(long Upload, long Download, long Total, long Expire)
{
    public const string HeaderName = "Subscription-Userinfo";

    public long Used => Upload + Download;

    /// <summary>Null for a missing or malformed header, or one with nothing in it.</summary>
    public static SvoRaySubscriptionUsage? Parse(string? header)
    {
        if (header.IsNullOrEmpty() || header!.Length > 512)
        {
            return null;
        }

        var values = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in header.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var pair = part.Split('=', 2, StringSplitOptions.TrimEntries);
            if (pair.Length == 2 && long.TryParse(pair[1], out var number) && number >= 0)
            {
                values[pair[0]] = number;
            }
        }

        if (!values.ContainsKey("upload") && !values.ContainsKey("download") && !values.ContainsKey("total"))
        {
            return null;
        }

        return new SvoRaySubscriptionUsage(
            values.GetValueOrDefault("upload"),
            values.GetValueOrDefault("download"),
            values.GetValueOrDefault("total"),
            values.GetValueOrDefault("expire"));
    }

    /// <summary>Compact storage form for the subscription row.</summary>
    public string Serialize() => $"upload={Upload}; download={Download}; total={Total}; expire={Expire}";

    /// <summary>Bytes as people read them: 87 GB, 1.2 TB, 640 MB.</summary>
    /// <param name="bytes">Amount to show.</param>
    /// <param name="unitList">Comma-separated B..TB names in the UI language.</param>
    public static string FormatBytes(long bytes, string? unitList = null)
    {
        var units = (unitList ?? "B,KB,MB,GB,TB").Split(',', StringSplitOptions.TrimEntries);
        if (units.Length != 5)
        {
            units = ["B", "KB", "MB", "GB", "TB"];
        }
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return unit == 0 || value >= 100
            ? $"{value:0} {units[unit]}"
            : $"{value:0.#} {units[unit]}";
    }
}
