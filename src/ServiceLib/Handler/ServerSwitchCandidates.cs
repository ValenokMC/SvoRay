namespace ServiceLib.Handler;

/// <summary>
/// Picks the servers a core is generated for, so that switching between them needs no restart.
/// The candidates are the selected server's own subscription: the list the user actually
/// switches within, and one whose routing rules are shared, so the rest of the config stays the
/// same whichever of them is used. sing-box gets all of them as outbounds behind a selector that
/// the Clash API switches; Xray keeps the one proxy outbound, which its handler API replaces, and
/// needs the others only so that their addresses are resolved outside the tunnel from the start.
/// </summary>
public static class ServerSwitchCandidates
{
    /// <summary>
    /// Above this the subscription is generated as before, with the selected server only. Every
    /// candidate is a DNS rule in the config and, for sing-box, an outbound; an unused outbound
    /// opens no connection, but there is no reason to grow the config without bound.
    /// </summary>
    public const int MaxCandidates = 200;

    /// <summary>
    /// Adds the switch candidates to <paramref name="context"/> when its core can be switched
    /// without a restart; otherwise returns it unchanged.
    /// </summary>
    public static async Task<CoreConfigContext> AttachAsync(CoreConfigContext context, CoreConfigContext? preContext)
    {
        if (!IsSwitchable(context, preContext))
        {
            return context;
        }

        var profiles = await AppManager.Instance.ProfileItems(context.Node.Subid) ?? [];
        var candidates = Select(context.Node, profiles,
            p => AppManager.Instance.GetCoreType(p, p.ConfigType));
        if (candidates.Count < 2)
        {
            return context;
        }

        // Each candidate is validated and registered like the selected server: it lands in the
        // proxy map, and a domain address joins the list resolved outside the tunnel.
        var accepted = new List<ProfileItem>();
        foreach (var candidate in candidates)
        {
            if (candidate.IndexId == context.Node.IndexId)
            {
                accepted.Add(context.Node);
                continue;
            }
            var (_, result) = await CoreConfigContextBuilder.ResolveNodeAsync(context, candidate, false);
            if (result.Success)
            {
                accepted.Add(candidate);
            }
        }

        return WithCandidates(context, accepted);
    }

    /// <summary>
    /// The context generated with <paramref name="candidates"/> behind the selector, already
    /// registered in its proxy map and protect list. Unchanged for fewer than two.
    /// </summary>
    public static CoreConfigContext WithCandidates(CoreConfigContext context, List<ProfileItem> candidates)
    {
        if (candidates.Count < 2)
        {
            return context;
        }
        // The set keeps insertion order, and the selected server was added first: sorted, the
        // DNS rule built from it is the same whichever candidate is selected.
        return context with
        {
            SwitchCandidates = candidates,
            ProtectDomainList = new HashSet<string>(context.ProtectDomainList.Order(StringComparer.Ordinal)),
        };
    }

    /// <summary>
    /// The core can only be switched in place for a plain server run by sing-box or Xray on its
    /// own. Groups, proxy chains (including a subscription's previous/next profile, which turns
    /// the server into a chain), custom configs, WireGuard (whose AmneziaWG relays start with the
    /// config) and a pre-socks core keep the restart path.
    /// </summary>
    public static bool IsSwitchable(CoreConfigContext context, CoreConfigContext? preContext)
    {
        var node = context.Node;
        return preContext is null
            && context.RunCoreType is ECoreType.sing_box or ECoreType.Xray
            && !node.Subid.IsNullOrEmpty()
            && !node.ConfigType.IsComplexType()
            && node.ConfigType != EConfigType.WireGuard;
    }

    /// <summary>
    /// The servers of <paramref name="selected"/>'s subscription that the same core can run.
    /// The order only has to be stable - it decides the outbound tags, and the same set must give
    /// the same config whichever server is selected - so it follows the profile ids.
    /// The selected server is always part of a non-empty result.
    /// </summary>
    public static List<ProfileItem> Select(ProfileItem selected, IEnumerable<ProfileItem> subscriptionProfiles,
        Func<ProfileItem, ECoreType> coreTypeOf)
    {
        var selectedCore = coreTypeOf(selected);
        var result = subscriptionProfiles
            .Where(p => p.Subid == selected.Subid)
            .Where(p => p.IndexId == selected.IndexId || IsCandidate(p, selectedCore, coreTypeOf))
            .DistinctBy(p => p.IndexId)
            .OrderBy(p => p.IndexId, StringComparer.Ordinal)
            .ToList();

        if (result.Count > MaxCandidates || result.All(p => p.IndexId != selected.IndexId))
        {
            return [];
        }
        return result;
    }

    private static bool IsCandidate(ProfileItem profile, ECoreType selectedCore, Func<ProfileItem, ECoreType> coreTypeOf)
    {
        return !profile.IndexId.IsNullOrEmpty()
            && !profile.ConfigType.IsComplexType()
            && profile.ConfigType != EConfigType.WireGuard
            && profile.IsValid()
            && coreTypeOf(profile) == selectedCore
            && CoreCanRun(selectedCore, profile);
    }

    /// <summary>The same checks the config generators make before they accept a server.</summary>
    private static bool CoreCanRun(ECoreType coreType, ProfileItem profile)
    {
        return coreType switch
        {
            ECoreType.sing_box => Global.SingboxSupportConfigType.Contains(profile.ConfigType)
                && profile.GetNetwork() is not (nameof(ETransport.kcp) or nameof(ETransport.xhttp)),
            ECoreType.Xray => Global.XraySupportConfigType.Contains(profile.ConfigType)
                && profile.GetNetwork() is not nameof(ETransport.quic),
            _ => false,
        };
    }

    /// <summary>Outbound tag of the candidate at <paramref name="index"/> in the selector.</summary>
    public static string OutboundTag(int index) => $"{Global.ProxyTag}-{index + 1}";
}
