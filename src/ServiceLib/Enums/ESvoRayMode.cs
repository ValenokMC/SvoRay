namespace ServiceLib.Enums;

/// <summary>
/// How simple mode routes traffic into the core.
/// </summary>
public enum ESvoRayMode
{
    /// <summary>Virtual adapter: the whole system goes through the core.</summary>
    Tun = 0,

    /// <summary>Windows system proxy: only applications that honour it.</summary>
    Proxy = 1
}
