namespace ServiceLib.Common;

/// <summary>Where the main window goes and which limits it takes in one of the two modes.</summary>
public readonly record struct SvoRayWindowBounds(
    double Left, double Top, double Width, double Height,
    double MinWidth, double MinHeight, double MaxWidth);

/// <summary>
/// The main window's two shapes. Simple mode is a phone-shaped window, advanced mode a desktop
/// one, and they cannot share a size or a minimum. Kept free of WPF so the arithmetic is tested;
/// MainWindow applies the result in one move (see its ApplyWindowSize).
/// </summary>
public static class SvoRayWindowLayout
{
    public const double SimpleWidth = 420;
    public const double SimpleHeight = 780;
    public const double SimpleMinWidth = 360;
    public const double SimpleMinHeight = 560;

    /// <summary>Past this the phone layout stops looking like one; the card stretches, nothing else.</summary>
    public const double SimpleMaxWidth = 560;

    public const double AdvancedWidth = 1180;
    public const double AdvancedHeight = 760;
    public const double AdvancedMinWidth = 860;
    public const double AdvancedMinHeight = 620;

    /// <summary>
    /// Limits that both shapes satisfy, set for the moment of the switch: the old mode's limits
    /// would otherwise clamp the new size on the way (the phone width cap on a desktop window).
    /// </summary>
    public const double RelaxedMinWidth = SimpleMinWidth;

    public const double RelaxedMinHeight = SimpleMinHeight;

    /// <summary>
    /// The bounds for a mode: the size the user last left it at in this session (or its default),
    /// kept within the mode's limits and the work area, and centred on the work area, so going
    /// back and forth never walks the window towards an edge.
    /// </summary>
    public static SvoRayWindowBounds For(bool simple, double? savedWidth, double? savedHeight,
        double workLeft, double workTop, double workWidth, double workHeight)
    {
        double width, height, minWidth, minHeight, maxWidth;
        if (simple)
        {
            minWidth = SimpleMinWidth;
            minHeight = SimpleMinHeight;
            maxWidth = SimpleMaxWidth;
            width = Math.Min(Math.Clamp(savedWidth ?? SimpleWidth, SimpleMinWidth, SimpleMaxWidth), workWidth);
            height = Math.Min(Math.Max(savedHeight ?? SimpleHeight, SimpleMinHeight), workHeight);
        }
        else
        {
            minWidth = AdvancedMinWidth;
            minHeight = AdvancedMinHeight;
            maxWidth = double.PositiveInfinity;
            width = Math.Min(Math.Max(savedWidth ?? AdvancedWidth, AdvancedMinWidth), workWidth);
            height = Math.Min(Math.Max(savedHeight ?? AdvancedHeight, AdvancedMinHeight), workHeight);
        }

        var left = workLeft + ((workWidth - width) / 2);
        var top = workTop + ((workHeight - height) / 2);
        return new SvoRayWindowBounds(left, top, width, height, minWidth, minHeight, maxWidth);
    }
}
