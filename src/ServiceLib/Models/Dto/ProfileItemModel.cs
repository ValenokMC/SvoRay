namespace ServiceLib.Models.Dto;

[Serializable]
public class ProfileItemModel : ReactiveObject
{
    public bool IsActive { get; set; }
    public string IndexId { get; set; }
    public EConfigType ConfigType { get; set; }
    public string Remarks { get; set; }
    public string Address { get; set; }
    public int Port { get; set; }
    public string Network { get; set; }
    public string StreamSecurity { get; set; }
    public string Subid { get; set; }
    public string SubRemarks { get; set; }
    public int Sort { get; set; }

    [Reactive]
    public int Delay { get; set; }

    public decimal Speed { get; set; }

    [Reactive]
    public string DelayVal { get; set; }

    [Reactive]
    public string SpeedVal { get; set; }

    [Reactive]
    public string IpInfo { get; set; }

    [Reactive]
    public string TodayUp { get; set; }

    [Reactive]
    public string TodayDown { get; set; }

    [Reactive]
    public string TotalUp { get; set; }

    [Reactive]
    public string TotalDown { get; set; }

    public string GetSummary()
    {
        var summary = $"[{ConfigType}] {Remarks}";
        if (!ConfigType.IsComplexType())
        {
            summary += $"({Address}:{Port})";
        }

        return summary;
    }

    /// <summary>
    /// Name for the simple screen and the tray menu. Deliberately carries no server address:
    /// the profile selector is on screen in every screenshot and screen share, and the
    /// address is the one part of a profile that points at the user's own infrastructure.
    /// The protocol tag is kept only as a fallback, when the subscription left no name.
    /// </summary>
    public string GetDisplayName()
    {
        return Remarks.IsNullOrEmpty() ? $"[{ConfigType}]" : Remarks;
    }
}
