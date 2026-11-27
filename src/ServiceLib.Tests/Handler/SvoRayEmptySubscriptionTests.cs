using ServiceLib.Tests.CoreConfig;

namespace ServiceLib.Tests.Handler;

// Inside the namespace: this scope is searched first, so Should() binds to AwesomeAssertions, not to TUnit's global Should().
using AwesomeAssertions;

/// <summary>
/// A subscription the simple screen added whose first download gave no servers is removed again,
/// so the scheduler cannot fill it in later as a duplicate of the one the user added instead.
/// One that has servers, or one that does not exist, is left alone.
/// </summary>
[NotInParallel(nameof(SvoRayEmptySubscriptionTests))]
public class SvoRayEmptySubscriptionTests
{
    private static async Task<(Config Config, string SubId)> CreateSubscriptionAsync(int servers)
    {
        var config = CoreConfigTestFactory.CreateConfig();
        CoreConfigTestFactory.BindAppManagerConfig(config);
        SQLiteHelper.Instance.CreateTable<SubItem>();
        SQLiteHelper.Instance.CreateTable<ProfileItem>();

        var subId = $"sub-{Guid.NewGuid():N}";
        await SQLiteHelper.Instance.ReplaceAsync(new SubItem { Id = subId, Remarks = "sub", Url = $"https://example.com/{subId}" });
        for (var i = 0; i < servers; i++)
        {
            var node = CoreConfigTestFactory.CreateSocksNode(ECoreType.Xray, $"{subId}-{i}", $"server-{i}");
            node.Subid = subId;
            await SQLiteHelper.Instance.ReplaceAsync(node);
        }
        return (config, subId);
    }

    [Test]
    public async Task RemoveSubscriptionIfEmpty_ShouldRemoveASubscriptionWithoutServers()
    {
        var (config, subId) = await CreateSubscriptionAsync(servers: 0);

        var removed = await ConfigHandler.RemoveSubscriptionIfEmpty(config, subId);

        removed.Should().BeTrue();
        (await AppManager.Instance.GetSubItem(subId)).Should().BeNull();
    }

    [Test]
    public async Task RemoveSubscriptionIfEmpty_ShouldKeepASubscriptionWithServers()
    {
        var (config, subId) = await CreateSubscriptionAsync(servers: 2);

        var removed = await ConfigHandler.RemoveSubscriptionIfEmpty(config, subId);

        removed.Should().BeFalse();
        (await AppManager.Instance.GetSubItem(subId)).Should().NotBeNull();
        (await AppManager.Instance.ProfileItems(subId) ?? []).Should().HaveCount(2);
    }

    [Test]
    public async Task RemoveSubscriptionIfEmpty_ShouldIgnoreAMissingSubscription()
    {
        var config = CoreConfigTestFactory.CreateConfig();
        CoreConfigTestFactory.BindAppManagerConfig(config);
        SQLiteHelper.Instance.CreateTable<SubItem>();

        (await ConfigHandler.RemoveSubscriptionIfEmpty(config, "no-such-subscription")).Should().BeFalse();
        (await ConfigHandler.RemoveSubscriptionIfEmpty(config, null)).Should().BeFalse();
    }
}
