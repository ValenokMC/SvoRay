using System.Windows.Media;

namespace v2rayN.Common;

/// <summary>
/// SvoRay's colours for the advanced interface. Unlike the simple screen, which is always dark,
/// the advanced interface follows the app's Light / Dark / System theme, so its palette has two
/// sides: dark is <see cref="SvoRayPalette"/> itself, light is the same graphite-and-mint palette
/// on light surfaces.
/// </summary>
/// <remarks>
/// SvoRayTheme.xaml binds the colour of each "SvoRay.Brush.*" brush to <see cref="Current"/>;
/// ThemeSettingViewModel calls <see cref="SetDark"/> whenever the base theme changes, and every
/// bound brush recolours in place. Those brushes cannot be frozen, but they change only on a
/// theme switch, never while the window animates.
/// </remarks>
public sealed class SvoRayAdaptivePalette : INotifyPropertyChanged
{
    public static SvoRayAdaptivePalette Current { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Dark until the theme says otherwise, the same as the simple screen.</summary>
    public bool IsDark { get; private set; } = true;

    /// <summary>Window background.</summary>
    public Color Background => IsDark ? SvoRayPalette.BackgroundColor : Rgb(0xF4, 0xF5, 0xF8);

    /// <summary>Rows, fields, cards and the tab strip.</summary>
    public Color Surface => IsDark ? SvoRayPalette.SurfaceColor : Rgb(0xFF, 0xFF, 0xFF);

    public Color SurfaceHover => IsDark ? SvoRayPalette.SurfaceHover.Color : Rgb(0xEE, 0xF0, 0xF4);
    public Color SurfacePressed => IsDark ? SvoRayPalette.SurfacePressed.Color : Rgb(0xE4, 0xE7, 0xED);

    public Color Border => IsDark ? SvoRayPalette.Border.Color : Rgb(0xDA, 0xDE, 0xE5);
    public Color BorderStrong => IsDark ? SvoRayPalette.BorderStrong.Color : Rgb(0xC6, 0xCB, 0xD5);

    public Color Text => IsDark ? SvoRayPalette.Text.Color : Rgb(0x15, 0x18, 0x1E);
    public Color TextSecondary => IsDark ? SvoRayPalette.TextSecondary.Color : Rgb(0x52, 0x59, 0x67);
    public Color TextMuted => IsDark ? SvoRayPalette.TextMuted.Color : Rgb(0x7C, 0x83, 0x91);

    /// <summary>Switch track when off.</summary>
    public Color Track => IsDark ? SvoRayPalette.Track.Color : Rgb(0xDC, 0xE0, 0xE6);

    /// <summary>
    /// Switch knob when off and when on. Light mode's off knob is grey: white on the pale track
    /// all but disappears.
    /// </summary>
    public Color Knob => IsDark ? SvoRayPalette.Text.Color : Rgb(0x7C, 0x83, 0x91);
    public Color KnobOn => IsDark ? SvoRayPalette.OnAccent.Color : Rgb(0xFF, 0xFF, 0xFF);

    public Color Danger => IsDark ? SvoRayPalette.DangerColor : Rgb(0xC9, 0x3C, 0x34);

    public void SetDark(bool isDark)
    {
        if (IsDark == isDark)
        {
            return;
        }

        IsDark = isDark;
        // An empty name refreshes every binding to this object at once.
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    }

    private static Color Rgb(byte r, byte g, byte b) => Color.FromRgb(r, g, b);
}
