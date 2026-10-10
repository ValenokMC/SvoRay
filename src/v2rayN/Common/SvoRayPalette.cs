using System.Windows.Media;

namespace v2rayN.Common;

/// <summary>
/// The simple screen's colours, in one place for XAML (<c>{x:Static common:SvoRayPalette.Accent}</c>)
/// and code alike. Graphite surfaces and a single mint accent: the accent means "on" - connected,
/// a switch turned on - and nothing else, so it never has to compete with decoration.
/// </summary>
/// <remarks>
/// Every brush is frozen. A frozen brush is shared across threads and never raises change
/// notifications, which keeps WPF from re-walking the visual tree when one is reused.
/// </remarks>
public static class SvoRayPalette
{
    public static readonly Color BackgroundColor = Color.FromRgb(0x0F, 0x11, 0x15);
    public static readonly Color SurfaceColor = Color.FromRgb(0x17, 0x1A, 0x20);
    public static readonly Color AccentColor = Color.FromRgb(0x3D, 0xDC, 0x97);
    public static readonly Color WarningColor = Color.FromRgb(0xE0, 0xA3, 0x3A);
    public static readonly Color DangerColor = Color.FromRgb(0xE5, 0x53, 0x4B);

    /// <summary>Window background.</summary>
    public static readonly SolidColorBrush Background = Make(BackgroundColor);

    /// <summary>Rows, fields and cards.</summary>
    public static readonly SolidColorBrush Surface = Make(SurfaceColor);
    public static readonly SolidColorBrush SurfaceHover = Make(Color.FromRgb(0x1E, 0x22, 0x2A));
    public static readonly SolidColorBrush SurfacePressed = Make(Color.FromRgb(0x24, 0x29, 0x32));

    public static readonly SolidColorBrush Border = Make(Color.FromRgb(0x26, 0x2A, 0x33));
    public static readonly SolidColorBrush BorderStrong = Make(Color.FromRgb(0x36, 0x3B, 0x47));

    public static readonly SolidColorBrush Text = Make(Color.FromRgb(0xE7, 0xE9, 0xEE));
    public static readonly SolidColorBrush TextSecondary = Make(Color.FromRgb(0x9A, 0xA0, 0xAD));
    public static readonly SolidColorBrush TextMuted = Make(Color.FromRgb(0x6B, 0x71, 0x80));

    public static readonly SolidColorBrush Accent = Make(AccentColor);
    public static readonly SolidColorBrush AccentHover = Make(Color.FromRgb(0x5A, 0xE6, 0xAA));
    /// <summary>Accent-tinted surface behind an "on" element.</summary>
    public static readonly SolidColorBrush AccentSurface = Make(Color.FromRgb(0x13, 0x24, 0x1D));
    public static readonly SolidColorBrush AccentBorder = Make(Color.FromRgb(0x2E, 0x6B, 0x52));
    /// <summary>Text on an accent fill.</summary>
    public static readonly SolidColorBrush OnAccent = Make(BackgroundColor);

    public static readonly SolidColorBrush Warning = Make(WarningColor);
    public static readonly SolidColorBrush WarningSurface = Make(Color.FromRgb(0x2A, 0x21, 0x12));
    public static readonly SolidColorBrush Danger = Make(DangerColor);
    public static readonly SolidColorBrush DangerSurface = Make(Color.FromRgb(0x2B, 0x15, 0x15));

    /// <summary>Switch track when off.</summary>
    public static readonly SolidColorBrush Track = Make(Color.FromRgb(0x2B, 0x30, 0x3A));

    /// <summary>Colour for a measured delay: quick, usable, slow.</summary>
    public static SolidColorBrush ForDelay(int milliseconds) => milliseconds switch
    {
        < 150 => Accent,
        < 400 => Warning,
        _ => Danger
    };

    private static SolidColorBrush Make(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
