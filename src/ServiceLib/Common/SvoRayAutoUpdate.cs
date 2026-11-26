namespace ServiceLib.Common;

/// <summary>
/// How SvoRay keeps what goes stale current by itself: the subscriptions (the server list and
/// the routing profile the provider sends with it) and v2rayN's own geo files. v2rayN leaves both
/// at "never", so a server list only changed when the user pressed refresh.
/// </summary>
public static class SvoRayAutoUpdate
{
    /// <summary>Minutes between refreshes of a subscription, for new ones and those moved off "never".</summary>
    public const int SubscriptionIntervalMinutes = 6 * 60;

    /// <summary>A refresh that failed (offline, provider down) is tried again this soon, not a whole interval later.</summary>
    public const int FailedRetryMinutes = 30;

    /// <summary>Hours between updates of <c>geoip.dat</c>, <c>geosite.dat</c> and the rule sets.</summary>
    public const int GeoFilesIntervalHours = 24;

    /// <summary>
    /// Moves the subscriptions of a config written by an earlier build, all at v2rayN's "never",
    /// to <see cref="SubscriptionIntervalMinutes"/>, once. Returns the ones it changed, to be
    /// saved; after that a 0 the user sets in the advanced settings stays.
    /// </summary>
    public static List<SubItem> MigrateSubscriptions(SvoRayItem settings, IEnumerable<SubItem> subscriptions)
    {
        if (settings.SubscriptionAutoUpdateMigrated)
        {
            return [];
        }

        var changed = subscriptions.Where(s => s.AutoUpdateInterval <= 0).ToList();
        foreach (var sub in changed)
        {
            sub.AutoUpdateInterval = SubscriptionIntervalMinutes;
        }
        settings.SubscriptionAutoUpdateMigrated = true;
        return changed;
    }

    /// <summary>
    /// Moves v2rayN's geo file update off "never" to <see cref="GeoFilesIntervalHours"/>, once;
    /// what the user sets afterwards stays.
    /// </summary>
    public static void MigrateGeoFiles(Config config)
    {
        if (config.SvoRayItem.GeoAutoUpdateMigrated)
        {
            return;
        }
        if (config.GuiItem.AutoUpdateInterval <= 0)
        {
            config.GuiItem.AutoUpdateInterval = GeoFilesIntervalHours;
        }
        config.SvoRayItem.GeoAutoUpdateMigrated = true;
    }

    /// <summary>Whether the scheduler refreshes <paramref name="sub"/> now (Unix seconds).</summary>
    public static bool IsSubscriptionDue(SubItem sub, long now)
    {
        return sub.Enabled
               && sub.AutoUpdateInterval > 0
               && now - sub.UpdateTime >= sub.AutoUpdateInterval * 60L;
    }

    /// <summary>
    /// What to store as the subscription's update time after an attempt at <paramref name="now"/>.
    /// After a failure it is set back so the next attempt comes after
    /// <see cref="FailedRetryMinutes"/> instead of a whole interval: a PC that was offline at
    /// the moment would otherwise keep a stale list for six more hours.
    /// </summary>
    public static long UpdateTimeAfterAttempt(SubItem sub, long now, bool success)
    {
        if (success || sub.AutoUpdateInterval <= FailedRetryMinutes)
        {
            return now;
        }
        return now - (sub.AutoUpdateInterval - FailedRetryMinutes) * 60L;
    }

    /// <summary>
    /// Whether v2rayN's geo files are due, from the age of <c>geosite.dat</c>: the scheduler
    /// used to count hours of uptime, so a PC switched off every night never reached 24.
    /// </summary>
    public static bool IsGeoUpdateDue(int intervalHours, DateTime? lastWriteUtc, DateTime nowUtc)
    {
        if (intervalHours <= 0)
        {
            return false;
        }
        if (lastWriteUtc is not { } last)
        {
            return true;
        }
        var age = nowUtc - last;
        return age >= TimeSpan.FromHours(intervalHours) || age <= -TimeSpan.FromHours(intervalHours);
    }
}
