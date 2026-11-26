namespace ServiceLib.Manager;

public enum ECoreSwitchKind
{
    /// <summary>Stop the running core and start a new one with the new config.</summary>
    Restart,

    /// <summary>Keep the running core and only change which server it sends traffic to.</summary>
    HotSwitch,
}

/// <summary>
/// What a generated config looks like from the point of view of a switch: everything except the
/// selected server, reduced to a hash, and the selected server itself. For sing-box the selection
/// is the outbound tag the server selector points at; for Xray it is the <see cref="Global.ProxyTag"/>
/// outbound, as JSON, that the running core gets in place of its current one.
/// </summary>
public sealed record CoreConfigShape(string Fingerprint, string Selection);

/// <summary>The core that is running now, the config it runs and how often it was switched in place.</summary>
public sealed record RunningCoreState(ECoreType CoreType, CoreConfigShape Shape, int HotSwitches = 0);

/// <summary>
/// Decides whether a new config can be applied to the running core without restarting it. That
/// is allowed only when the new config is the running one with another server selected: same
/// core, same inbounds, TUN, DNS and routing, and for sing-box the same set of servers in the
/// selector. Anything else - another core type, a mode change, a routing change, a subscription
/// update that changed the server set - restarts the core as before.
/// </summary>
public static class CoreSwitchPlanner
{
    /// <summary>
    /// Xray does not close an outbound it removes, so every swap leaves the old one behind until
    /// its connections end (a QUIC-based one may keep its connection alive). After this many
    /// swaps the next switch restarts the core once, which clears them all.
    /// </summary>
    public const int MaxXrayHotSwitches = 20;

    /// <summary>
    /// Reads the switchable part of a sing-box config: the <see cref="Global.ProxyTag"/> selector
    /// and its <c>default</c>. Returns null for configs without such a selector (a single server,
    /// another core's config, a policy group with automatic selection), which always restart.
    /// </summary>
    public static CoreConfigShape? ReadSingboxShape(string? configJson)
    {
        if (ParseObject(configJson) is not { } root || root["outbounds"] is not JsonArray outbounds)
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
        return new CoreConfigShape(Hash(root), selectedTag!);
    }

    /// <summary>
    /// Reads the switchable part of an Xray config: the single <see cref="Global.ProxyTag"/>
    /// outbound, which the handler API can replace in the running core. Returns null when the
    /// config has no handler API to do that with, or when the proxy is not one plain outbound
    /// (balancers, chains), which always restart.
    /// </summary>
    public static CoreConfigShape? ReadXrayShape(string? configJson)
    {
        if (ParseObject(configJson) is not { } root || root["outbounds"] is not JsonArray outbounds)
        {
            return null;
        }
        if (root["api"] is not JsonObject api
            || api["services"] is not JsonArray services
            || !services.Select(s => (string?)s).Contains(XrayApiService.HandlerService)
            || ((string?)api["listen"]).IsNullOrEmpty())
        {
            return null;
        }
        if (root["routing"]?["balancers"] is JsonArray { Count: > 0 })
        {
            return null;
        }

        var proxies = outbounds
            .OfType<JsonObject>()
            .Where(o => (string?)o["tag"] == Global.ProxyTag)
            .ToList();
        if (proxies.Count != 1)
        {
            return null;
        }

        var proxy = proxies[0];
        var selection = proxy.ToJsonString();
        outbounds.Remove(proxy);
        return new CoreConfigShape(Hash(root), selection);
    }

    public static ECoreSwitchKind Decide(RunningCoreState? running, bool coreAlive, ECoreType newCoreType,
        bool hasPreCore, CoreConfigShape? newShape)
    {
        if (running is null || !coreAlive || hasPreCore || newShape is null)
        {
            return ECoreSwitchKind.Restart;
        }
        if (running.CoreType != newCoreType || newCoreType is not (ECoreType.sing_box or ECoreType.Xray))
        {
            return ECoreSwitchKind.Restart;
        }
        if (running.Shape.Fingerprint != newShape.Fingerprint)
        {
            return ECoreSwitchKind.Restart;
        }
        if (newCoreType == ECoreType.Xray && running.HotSwitches >= MaxXrayHotSwitches)
        {
            return ECoreSwitchKind.Restart;
        }
        // The same server again is a reload asked for on purpose (the reload menu item, a
        // recovery attempt); it keeps restarting the core as it always did.
        return running.Shape.Selection == newShape.Selection
            ? ECoreSwitchKind.Restart
            : ECoreSwitchKind.HotSwitch;
    }

    /// <summary>
    /// A digest of everything the core would run: the main config and the pre core's, if there
    /// is one. The dated log file names are left out - they change at midnight while nothing a
    /// connection depends on does, and a running core keeps writing where it started anyway.
    /// Null when there is nothing to compare (a custom config, a config that is not JSON).
    /// </summary>
    public static string? ReadConfigDigest(string? mainConfigJson, string? preConfigJson = null)
    {
        if (ParseObject(mainConfigJson) is not { } main)
        {
            return null;
        }
        var text = WithoutLogFiles(main).ToJsonString();
        if (preConfigJson is not null)
        {
            if (ParseObject(preConfigJson) is not { } pre)
            {
                return null;
            }
            text += "\n" + WithoutLogFiles(pre).ToJsonString();
        }
        return Hash(text);
    }

    /// <summary>
    /// For a reload nobody asked for by hand - a background subscription refresh, a routing list
    /// rebuilt after it: whether the running core can be left as it is because the config it
    /// would get is the one it already runs. Restarting it would drop every call and game for
    /// nothing. Anything else - another config, a core that died, nothing running - reloads.
    /// <see cref="Decide"/> still restarts for the same config when the user reloads on purpose.
    /// </summary>
    public static bool CanKeepRunningCore(string? runningDigest, bool coreAlive, string? newDigest)
    {
        return coreAlive && runningDigest.IsNotEmpty() && runningDigest == newDigest;
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

    private static JsonObject? ParseObject(string? json)
    {
        if (json.IsNullOrEmpty())
        {
            return null;
        }
        try
        {
            return JsonNode.Parse(json) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static JsonObject WithoutLogFiles(JsonObject root)
    {
        if (root["log"] is JsonObject log)
        {
            // Xray: access/error; sing-box: output.
            log.Remove("access");
            log.Remove("error");
            log.Remove("output");
        }
        return root;
    }

    private static string Hash(JsonNode root)
    {
        return Hash(root.ToJsonString());
    }

    private static string Hash(string text)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }
}
