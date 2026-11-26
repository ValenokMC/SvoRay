using AwesomeAssertions;
using Xunit;

namespace ServiceLib.Tests.Common;

public class SvoRayAutoUpdateTests
{
    private const long Now = 1_800_000_000;

    [Fact]
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

    [Fact]
    public void MigrateSubscriptions_KeepsNeverChosenAfterTheMigration()
    {
        var settings = new SvoRayItem { SubscriptionAutoUpdateMigrated = true };
        var never = new SubItem { Id = "a", AutoUpdateInterval = 0 };

        SvoRayAutoUpdate.MigrateSubscriptions(settings, [never]).Should().BeEmpty();

        never.AutoUpdateInterval.Should().Be(0);
    }

    [Fact]
    public void MigrateSubscriptions_FlagSurvivesTheConfigFile()
    {
        var settings = JsonUtils.Deserialize<SvoRayItem>(
            JsonUtils.Serialize(new SvoRayItem { SubscriptionAutoUpdateMigrated = true, LastGeoUpdate = Now }))!;

        settings.SubscriptionAutoUpdateMigrated.Should().BeTrue();
        settings.LastGeoUpdate.Should().Be(Now);
    }

    [Fact]
    public void NewSubscription_RefreshesByItself()
    {
        new SubItem().AutoUpdateInterval.Should().Be(SvoRayAutoUpdate.SubscriptionIntervalMinutes);
        SvoRayAutoUpdate.SubscriptionIntervalMinutes.Should().Be(360);
    }

    [Theory]
    [InlineData(0)] // off, v2rayN's default: about 35 MB a day is not turned on for anyone
    [InlineData(72)]
    public void MigrateSvoRayItem_LeavesTheGeoFileIntervalAsTheUserSetIt(int interval)
    {
        var config = JsonUtils.Deserialize<Config>($$"""{ "GuiItem": { "AutoUpdateInterval": {{interval}} }, "SvoRayItem": {} }""")!;

        ConfigHandler.MigrateSvoRayItem(config);

        config.GuiItem.AutoUpdateInterval.Should().Be(interval);
    }

    [Theory]
    [InlineData(360, 0, true, true)] // never refreshed yet: right away
    [InlineData(360, Now - 360 * 60, true, true)]
    [InlineData(360, Now - 359 * 60, true, false)]
    [InlineData(0, 0, true, false)] // "never" chosen by the user
    [InlineData(360, 0, false, false)] // switched off subscription
    public void IsSubscriptionDue_FollowsTheInterval(int interval, long updateTime, bool enabled, bool expected)
    {
        var sub = new SubItem { AutoUpdateInterval = interval, UpdateTime = updateTime, Enabled = enabled };

        SvoRayAutoUpdate.IsSubscriptionDue(sub, Now).Should().Be(expected);
    }

    [Fact]
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

    [Fact]
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
