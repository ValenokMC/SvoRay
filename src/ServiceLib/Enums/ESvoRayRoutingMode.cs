namespace ServiceLib.Enums;

/// <summary>
/// What the simple-mode domain list means.
/// </summary>
public enum ESvoRayRoutingMode
{
    /// <summary>Everything goes through the VPN except the listed domains.</summary>
    BypassListed = 0,

    /// <summary>Only the listed domains go through the VPN; everything else stays direct.</summary>
    ProxyListed = 1
}
