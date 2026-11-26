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
            JsonUtils.Serialize(new SvoRayItem { SubscriptionAutoUpdateMigrated = true, GeoAutoUpdateMigrated = true }))!;

        settings.SubscriptionAutoUpdateMigrated.Should().BeTrue();
        settings.GeoAutoUpdateMigrated.Should().BeTrue();
    }

    [Fact]
    public void NewSubscription_RefreshesByItself()
    {
        new SubItem().AutoUpdateInterval.Should().Be(SvoRayAutoUpdate.SubscriptionIntervalMinutes);
        SvoRayAutoUpdate.SubscriptionIntervalMinutes.Should().Be(360);
    }

    [Fact]
    public void MigrateSvoRayItem_MovesGeoFileUpdatesOffNeverOnce()
    {
        var config = JsonUtils.Deserialize<Config>("""{ "GuiItem": { "AutoUpdateInterval": 0 }, "SvoRayItem": {} }""")!;

        ConfigHandler.MigrateSvoRayItem(config);

        config.GuiItem.AutoUpdateInterval.Should().Be(SvoRayAutoUpdate.GeoFilesIntervalHours);
        config.SvoRayItem.GeoAutoUpdateMigrated.Should().BeTrue();

        config.GuiItem.AutoUpdateInterval = 0;
        ConfigHandler.MigrateSvoRayItem(config);
        config.GuiItem.AutoUpdateInterval.Should().Be(0, "turned off by the user after the migration");
    }

    [Fact]
    public void MigrateSvoRayItem_KeepsAGeoIntervalTheUserSet()
    {
        var config = JsonUtils.Deserialize<Config>("""{ "GuiItem": { "AutoUpdateInterval": 72 }, "SvoRayItem": {} }""")!;

        ConfigHandler.MigrateSvoRayItem(config);

        config.GuiItem.AutoUpdateInterval.Should().Be(72);
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
    public void IsGeoUpdateDue_GoesByTheAgeOfTheFile()
    {
        var now = new DateTime(2026, 11, 26, 12, 0, 0, DateTimeKind.Utc);

        SvoRayAutoUpdate.IsGeoUpdateDue(24, null, now).Should().BeTrue("no file yet");
        SvoRayAutoUpdate.IsGeoUpdateDue(24, now.AddHours(-25), now).Should().BeTrue();
        SvoRayAutoUpdate.IsGeoUpdateDue(24, now.AddHours(-23), now).Should().BeFalse();
        SvoRayAutoUpdate.IsGeoUpdateDue(24, now.AddDays(30), now).Should().BeTrue("a file dated by a wrong clock");
        SvoRayAutoUpdate.IsGeoUpdateDue(0, now.AddDays(-300), now).Should().BeFalse("never");
    }
}
