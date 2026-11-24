namespace ServiceLib.Manager;

public enum ECoreSwitchKind
{
    /// <summary>Stop the running core and start a new one with the new config.</summary>
    Restart,

    /// <summary>Keep the running core and point its server selector at another server.</summary>
    HotSwitch,
}

/// <summary>
/// What a generated config looks like from the point of view of a switch: everything except the
/// server the selector currently points at, reduced to a hash, and that server's outbound tag.
/// </summary>
public sealed record CoreConfigShape(string Fingerprint, string SelectedTag);

/// <summary>The core that is running now and the config it was started with.</summary>
public sealed record RunningCoreState(ECoreType CoreType, CoreConfigShape Shape);

/// <summary>
/// Decides whether a new config can be applied to the running core by switching its server
/// selector, or needs a restart. A hot switch is allowed only when the new config is the running
/// one with a different server selected: same core, same inbounds, TUN, DNS and routing, the same
/// set of servers in the selector. Anything else - another core type, a mode change, a routing
/// change, a subscription update that changed the server set - restarts the core as before.
/// </summary>
public static class CoreSwitchPlanner
{
    /// <summary>
    /// Reads the switchable part of a sing-box config: the <see cref="Global.ProxyTag"/> selector
    /// and its <c>default</c>. Returns null for configs without such a selector (a single server,
    /// another core's config, a policy group with automatic selection), which always restart.
    /// </summary>
    public static CoreConfigShape? ReadSingboxShape(string? configJson)
    {
        if (configJson.IsNullOrEmpty())
        {
            return null;
        }

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(configJson);
        }
        catch (JsonException)
        {
            return null;
        }

        if (root?["outbounds"] is not JsonArray outbounds)
        {
            return null;
        }

        var selector = outbounds
            .OfType<JsonObject>()
            .FirstOrDefault(o => (string?)o["tag"] == Global.ProxyTag && (string?)o["type"] == "selector");
        var selectedTag = (string?)selector?["default"];
        if (selector is null || selectedTag.IsNullOrEmpty())
        {
            return null;
        }
        // An automatic group (urltest inside the selector) picks servers on its own; only a
        // selector whose members are all plain servers is switched by hand.
        if (selector["outbounds"] is not JsonArray members
            || !members.Select(m => (string?)m).Contains(selectedTag))
        {
            return null;
        }

        selector.Remove("default");
        var normalized = root.ToJsonString();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
        return new CoreConfigShape(hash, selectedTag!);
    }

    public static ECoreSwitchKind Decide(RunningCoreState? running, bool coreAlive, ECoreType newCoreType,
        bool hasPreCore, CoreConfigShape? newShape)
    {
        if (running is null || !coreAlive || hasPreCore || newShape is null)
        {
            return ECoreSwitchKind.Restart;
        }
        if (running.CoreType != ECoreType.sing_box || newCoreType != ECoreType.sing_box)
        {
            return ECoreSwitchKind.Restart;
        }
        if (running.Shape.Fingerprint != newShape.Fingerprint)
        {
            return ECoreSwitchKind.Restart;
        }
        // The same server again is a reload asked for on purpose (the reload menu item, a
        // recovery attempt); it keeps restarting the core as it always did.
        return running.Shape.SelectedTag == newShape.SelectedTag
            ? ECoreSwitchKind.Restart
            : ECoreSwitchKind.HotSwitch;
    }

    /// <summary>
    /// Whether the stale Wintun adapters must be removed before a TUN core starts. Removing them
    /// takes two pnputil runs, about a second each, and a core that was stopped normally leaves
    /// nothing behind: the adapter goes away with the process. It is still done for the first
    /// TUN start of the session (the previous run may have crashed) and after the core died on
    /// its own; a start that fails anyway is retried once after removing them.
    /// </summary>
    public static bool ShouldRemoveTunDevices(bool isWindows, bool isTun, bool tunDeviceMayBeStale)
    {
        return isWindows && isTun && tunDeviceMayBeStale;
    }
}
