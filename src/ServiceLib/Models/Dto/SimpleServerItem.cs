namespace ServiceLib.Models.Dto;

/// <summary>
/// A row of the simple screen's server list. Only <see cref="ESimpleServerItemKind.Server"/>
/// rows carry an <see cref="ComboItem.ID"/> and can be selected; the others group the servers
/// and hold the actions. <see cref="ComboItem.Text"/> is what the closed selector shows,
/// <see cref="ListText"/> what the open list shows under its heading.
/// </summary>
public class SimpleServerItem : ComboItem
{
    public ESimpleServerItemKind Kind { get; init; }

    public string ListText { get; init; } = string.Empty;

    /// <summary>The full name without its flag emoji, which the screen draws as a picture instead.</summary>
    public string ShortText { get; init; } = string.Empty;

    /// <summary>Lower-case country code of the flag shown before the name; null when there is none.</summary>
    public string? FlagCode { get; init; }

    /// <summary>The subscription a heading stands for.</summary>
    public string? SubId { get; init; }

    /// <summary>The name used in the removal question.</summary>
    public string SourceLabel { get; init; } = string.Empty;

    public int Count { get; init; }

    /// <summary>A subscription heading, or a server that belongs to no subscription.</summary>
    public bool CanRemove { get; init; }

    public bool IsServer => Kind == ESimpleServerItemKind.Server;
}
