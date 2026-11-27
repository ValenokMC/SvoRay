namespace ServiceLib.Tests.Common;

// Inside the namespace: this scope is searched first, so Should() binds to AwesomeAssertions, not to TUnit's global Should().
using AwesomeAssertions;

[NotInParallel(nameof(SvoRayAutoUpdateTests))]
public class SvoRayAutoUpdateTests
{
    private const long Now = 1_800_000_000;

    [Test]
    public void MigrateSubscriptions_MovesSubscriptionsLeftAtNeverOnce()
    {
        // As found on a real install: every subscription at v2rayN's "never".
        var settings = new SvoRayItem();
        var never = new SubItem { Id = "a", AutoUpdateInterval = 0 };
        var chosen = new SubItem { Id = "b", AutoUpdateInterval = 90 };

        var changed = SvoRayAutoUpdate.MigrateSubscriptions(settings, [never, chosen]);

        changed.Should().ContainSingle().Which.Id.Should().Be("a");
        never.AutoUpdateInterval.Should().Be(SvoRayAutoUpdate.SubscriptionIntervalMinutes);
        chosen.AutoUpdateInterval.Should().Be(90, "an interval the user typed is theirs");
        settings.SubscriptionAutoUpdateMigrated.Should().BeTrue();
    }

    [Test]
    public void MigrateSubscriptions_KeepsNeverChosenAfterTheMigration()
    {
        var settings = new SvoRayItem { SubscriptionAutoUpdateMigrated = true };
        var never = new SubItem { Id = "a", AutoUpdateInterval = 0 };

        SvoRayAutoUpdate.MigrateSubscriptions(settings, [never]).Should().BeEmpty();

        never.AutoUpdateInterval.Should().Be(0);
    }

    [Test]
    public void MigrateSubscriptions_FlagSurvivesTheConfigFile()
    {
        var settings = JsonUtils.Deserialize<SvoRayItem>(
            JsonUtils.Serialize(new SvoRayItem { SubscriptionAutoUpdateMigrated = true, LastGeoUpdate = Now }))!;

        settings.SubscriptionAutoUpdateMigrated.Should().BeTrue();
        settings.LastGeoUpdate.Should().Be(Now);
    }

    [Test]
    public void NewSubscription_RefreshesByItself()
    {
        new SubItem().AutoUpdateInterval.Should().Be(SvoRayAutoUpdate.SubscriptionIntervalMinutes);
        SvoRayAutoUpdate.SubscriptionIntervalMinutes.Should().Be(360);
    }

    [Test]
    [Arguments(0)] // off, v2rayN's default: about 35 MB a day is not turned on for anyone
    [Arguments(72)]
    public void MigrateSvoRayItem_LeavesTheGeoFileIntervalAsTheUserSetIt(int interval)
    {
        var config = JsonUtils.Deserialize<Config>($$"""{ "GuiItem": { "AutoUpdateInterval": {{interval}} }, "SvoRayItem": {} }""")!;

        ConfigHandler.MigrateSvoRayItem(config);

        config.GuiItem.AutoUpdateInterval.Should().Be(interval);
    }

    [Test]
    [Arguments(360, 0, true, true)] // never refreshed yet: right away
    [Arguments(360, Now - 360 * 60, true, true)]
    [Arguments(360, Now - 359 * 60, true, false)]
    [Arguments(0, 0, true, false)] // "never" chosen by the user
    [Arguments(360, 0, false, false)] // switched off subscription
    public void IsSubscriptionDue_FollowsTheInterval(int interval, long updateTime, bool enabled, bool expected)
    {
        var sub = new SubItem { AutoUpdateInterval = interval, UpdateTime = updateTime, Enabled = enabled };

        SvoRayAutoUpdate.IsSubscriptionDue(sub, Now).Should().Be(expected);
    }

    [Test]
    public void UpdateTimeAfterAttempt_RetriesAFailureSoon()
    {
        var sub = new SubItem { AutoUpdateInterval = 360 };

        SvoRayAutoUpdate.UpdateTimeAfterAttempt(sub, Now, success: true).Should().Be(Now);

        sub.UpdateTime = SvoRayAutoUpdate.UpdateTimeAfterAttempt(sub, Now, success: false);
        SvoRayAutoUpdate.IsSubscriptionDue(sub, Now + 29 * 60).Should().BeFalse();
        SvoRayAutoUpdate.IsSubscriptionDue(sub, Now + SvoRayAutoUpdate.FailedRetryMinutes * 60).Should().BeTrue();

        var frequent = new SubItem { AutoUpdateInterval = 10 };
        SvoRayAutoUpdate.UpdateTimeAfterAttempt(frequent, Now, success: false).Should().Be(Now);
    }

    [Test]
    public void IsGeoUpdateDue_GoesByTheLastUpdateNotTheUptime()
    {
        const long hour = 3600;

        SvoRayAutoUpdate.IsGeoUpdateDue(24, 0, Now).Should().BeTrue("never updated yet");
        SvoRayAutoUpdate.IsGeoUpdateDue(24, Now - 25 * hour, Now).Should().BeTrue();
        SvoRayAutoUpdate.IsGeoUpdateDue(24, Now - 23 * hour, Now).Should().BeFalse();
        SvoRayAutoUpdate.IsGeoUpdateDue(24, Now + 30 * 24 * hour, Now).Should().BeTrue("written under a wrong clock");
        SvoRayAutoUpdate.IsGeoUpdateDue(0, Now - 300 * 24 * hour, Now).Should().BeFalse("off");
    }
}
