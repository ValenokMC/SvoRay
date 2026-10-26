using AwesomeAssertions;
using Xunit;

namespace ServiceLib.Tests.Common;

public class SvoRayServerListTests
{
    private const string Standalone = "Servers added on their own";
    private const string Add = "Add a subscription or file…";

    private static ProfileItemModel Profile(string indexId, string remarks, string subId = "") => new()
    {
        IndexId = indexId,
        Remarks = remarks,
        Address = "198.51.100.1",
        Port = 443,
        Subid = subId,
    };

    private static SubItem Sub(string id, string remarks, string url = "https://example.com/sub") => new()
    {
        Id = id,
        Remarks = remarks,
        Url = url,
    };

    [Fact]
    public void Build_ShouldGroupServersUnderTheirSourceAndEndWithAdd()
    {
        var profiles = new List<ProfileItemModel>
        {
            Profile("warp", "Cloudflare WARP"),
            Profile("nl", "Netherlands", "sub-a"),
            Profile("de", "Germany", "sub-b"),
            Profile("fi", "Finland", "sub-a"),
        };
        var subs = new List<SubItem> { Sub("sub-a", "MyVPN"), Sub("sub-b", "Backup") };

        var (servers, grouped) = SvoRayServerList.Build(profiles, subs, Standalone, Add);

        grouped.Select(t => (t.Kind, t.ListText)).Should().Equal(
            (ESimpleServerItemKind.Subscription, "MyVPN · 2"),
            (ESimpleServerItemKind.Server, "Netherlands"),
            (ESimpleServerItemKind.Server, "Finland"),
            (ESimpleServerItemKind.Subscription, "Backup · 1"),
            (ESimpleServerItemKind.Server, "Germany"),
            (ESimpleServerItemKind.Standalone, Standalone),
            (ESimpleServerItemKind.Server, "Cloudflare WARP"),
            (ESimpleServerItemKind.Add, Add));

        servers.Select(t => t.ID).Should().Equal("nl", "fi", "de", "warp");
        grouped.Where(t => t.IsServer).Should().Equal(servers, "both selectors must share the selected object");
    }

    [Fact]
    public void Build_ClosedSelectorShouldNameTheSourceOnlyWithSeveralSources()
    {
        var subs = new List<SubItem> { Sub("sub-a", "MyVPN") };

        var (single, _) = SvoRayServerList.Build([Profile("nl", "Netherlands", "sub-a")], subs, Standalone, Add);
        single[0].Text.Should().Be("Netherlands");

        var (several, _) = SvoRayServerList.Build(
            [Profile("nl", "Netherlands", "sub-a"), Profile("warp", "Cloudflare WARP")], subs, Standalone, Add);
        several[0].Text.Should().Be("MyVPN · Netherlands");
        several[1].Text.Should().Be("Cloudflare WARP");
    }

    [Fact]
    public void Build_OnlyHeadingsOfSubscriptionsAndStandaloneServersCanBeRemoved()
    {
        var (_, grouped) = SvoRayServerList.Build(
            [Profile("nl", "Netherlands", "sub-a"), Profile("warp", "Cloudflare WARP"), Profile("lost", "Orphan", "deleted-sub")],
            [Sub("sub-a", "MyVPN")], Standalone, Add);

        grouped.Where(t => t.CanRemove).Select(t => t.ListText)
            .Should().Equal("MyVPN · 1", "Cloudflare WARP", "Orphan");
        grouped.Single(t => t.Kind == ESimpleServerItemKind.Subscription).SubId.Should().Be("sub-a");
        grouped.Where(t => !t.IsServer).Should().OnlyContain(t => t.ID == null, "nothing but a server may be selected");
    }

    [Fact]
    public void Build_EmptySubscriptionWithLinkStaysRemovable()
    {
        var (_, grouped) = SvoRayServerList.Build([],
            [Sub("broken", "Broken"), Sub("local", "Local", url: "")], Standalone, Add);

        grouped.Select(t => (t.Kind, t.ListText)).Should().Equal(
            (ESimpleServerItemKind.Subscription, "Broken · 0"),
            (ESimpleServerItemKind.Add, Add));
    }
}
