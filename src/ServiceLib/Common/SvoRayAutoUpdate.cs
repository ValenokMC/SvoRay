namespace ServiceLib.Common;

/// <summary>
/// How SvoRay keeps what goes stale current by itself: the subscriptions (the server list and
/// the routing profile the provider sends with it). v2rayN leaves them at "never", so a server
/// list only changed when the user pressed refresh. v2rayN's own geo files keep the user's
/// setting, off by default: SvoRay uses only their private ranges, which come with each release,
/// and a daily update is about 35 MB, often through the VPN and on metered plans.
/// </summary>
public static class SvoRayAutoUpdate
{
    /// <summary>Minutes between refreshes of a subscription, for new ones and those moved off "never".</summary>
    public const int SubscriptionIntervalMinutes = 6 * 60;

    /// <summary>A refresh that failed (offline, provider down) is tried again this soon, not a whole interval later.</summary>
    public const int FailedRetryMinutes = 30;

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
    /// Whether v2rayN's geo files are due, for a user who turned their update on: from the time
    /// of the last update (Unix seconds, 0 for none), not from hours of uptime, which a PC
    /// switched off every night never brought to 24. A time far in the future (a wrong clock) is
    /// due too, or it would hold the update back for as long.
    /// </summary>
    public static bool IsGeoUpdateDue(int intervalHours, long lastUpdate, long now)
    {
        if (intervalHours <= 0)
        {
            return false;
        }
        if (lastUpdate <= 0)
        {
            return true;
        }
        var interval = intervalHours * 3600L;
        var age = now - lastUpdate;
        return age >= interval || age <= -interval;
    }
}
