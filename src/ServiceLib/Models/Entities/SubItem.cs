namespace ServiceLib.Models.Entities;

[Serializable]
public class SubItem
{
    [PrimaryKey]
    public string Id { get; set; }

    public string Remarks { get; set; }

    public string Url { get; set; }

    public string MoreUrl { get; set; }

    public bool Enabled { get; set; } = true;

    public string UserAgent { get; set; } = string.Empty;

    public int Sort { get; set; }

    public string? Filter { get; set; }

    public int AutoUpdateInterval { get; set; }

    public long UpdateTime { get; set; }

    public string? ConvertTarget { get; set; }

    public string? PrevProfile { get; set; }

    public string? NextProfile { get; set; }

    public int? PreSocksPort { get; set; }

    public string? Memo { get; set; }

    public string? SupportUrl { get; set; }

    /// <summary>
    /// Routing profile from the server's <c>routing</c> header, validated and stored as JSON;
    /// null when the server sends none. See <see cref="SvoRayServerRouting"/>.
    /// </summary>
    public string? RoutingProfile { get; set; }

    /// <summary>
    /// Last <c>Subscription-Userinfo</c> the server sent, normalised; null when it sends none.
    /// See <see cref="SvoRaySubscriptionUsage"/>.
    /// </summary>
    public string? UsageInfo { get; set; }

    /// <summary>
    /// The provider's name from the <c>profile-title</c> header; null when it sends none.
    /// See <see cref="SvoRaySubscriptionLabel"/>.
    /// </summary>
    public string? ProfileTitle { get; set; }
}
