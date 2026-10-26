namespace ServiceLib.Common;

/// <summary>
/// Builds the simple screen's server list: every server of every subscription, in the order of
/// the subscriptions, then the servers added on their own. The flat list feeds the tray; the
/// grouped one puts a heading over each source, so a subscription can be removed from the list
/// itself, and ends with a row that adds another source.
/// </summary>
public static class SvoRayServerList
{
    public static (List<SimpleServerItem> Servers, List<SimpleServerItem> Grouped) Build(
        IReadOnlyList<ProfileItemModel> profiles,
        IReadOnlyList<SubItem> subs,
        string standaloneTitle,
        string addTitle)
    {
        var order = subs.Select((sub, index) => (sub.Id, index)).ToDictionary(t => t.Id, t => t.index);
        var labels = subs.ToDictionary(sub => sub.Id, SvoRaySubscriptionLabel.For);
        string? SubIdOf(ProfileItemModel profile) =>
            profile.Subid.IsNotEmpty() && order.ContainsKey(profile.Subid) ? profile.Subid : null;

        var severalSources = profiles.Select(SubIdOf).Distinct().Count() > 1;
        var servers = profiles
            .Select((profile, position) => (profile, position))
            .OrderBy(t => SubIdOf(t.profile) is { } subId ? order[subId] : int.MaxValue)
            .ThenBy(t => t.position)
            .Select(t =>
            {
                var subId = SubIdOf(t.profile);
                var name = t.profile.GetDisplayName();
                return new SimpleServerItem
                {
                    Kind = ESimpleServerItemKind.Server,
                    ID = t.profile.IndexId,
                    Text = SvoRaySubscriptionLabel.Compose(subId is null ? null : labels[subId], name, severalSources),
                    ListText = name,
                    SubId = subId,
                    SourceLabel = name,
                    CanRemove = subId is null,
                };
            })
            .ToList();

        var grouped = new List<SimpleServerItem>();
        foreach (var sub in subs)
        {
            var own = servers.Where(t => t.SubId == sub.Id).ToList();
            // An empty subscription stays listed while it has a link, so a broken one can still be removed.
            if (own.Count == 0 && sub.Url.IsNullOrEmpty())
            {
                continue;
            }
            grouped.Add(new SimpleServerItem
            {
                Kind = ESimpleServerItemKind.Subscription,
                Text = $"{labels[sub.Id]} · {own.Count}",
                ListText = $"{labels[sub.Id]} · {own.Count}",
                SubId = sub.Id,
                SourceLabel = labels[sub.Id],
                Count = own.Count,
                CanRemove = true,
            });
            grouped.AddRange(own);
        }

        var standalone = servers.Where(t => t.SubId is null).ToList();
        if (standalone.Count > 0)
        {
            grouped.Add(new SimpleServerItem
            {
                Kind = ESimpleServerItemKind.Standalone,
                Text = standaloneTitle,
                ListText = standaloneTitle,
                Count = standalone.Count,
            });
            grouped.AddRange(standalone);
        }

        grouped.Add(new SimpleServerItem
        {
            Kind = ESimpleServerItemKind.Add,
            Text = addTitle,
            ListText = addTitle,
        });

        return (servers, grouped);
    }
}
