using System.Windows.Media.Animation;

namespace v2rayN.Common;

/// <summary>
/// Lets WPF animations run at the monitor's refresh rate. WPF caps every animation clock at
/// 60 frames per second by default, so on a 120 or 144 Hz screen a switch or a pulse moves in
/// visibly coarser steps than the cursor and the rest of the desktop.
/// </summary>
internal static class SvoRayDisplay
{
    private const int EnumCurrentSettings = -1;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DevMode
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public short dmSpecVersion;
        public short dmDriverVersion;
        public short dmSize;
        public short dmDriverExtra;
        public int dmFields;
        public int dmPositionX;
        public int dmPositionY;
        public int dmDisplayOrientation;
        public int dmDisplayFixedOutput;
        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public short dmLogPixels;
        public int dmBitsPerPel;
        public int dmPelsWidth;
        public int dmPelsHeight;
        public int dmDisplayFlags;
        public int dmDisplayFrequency;
        public int dmICMMethod;
        public int dmICMIntent;
        public int dmMediaType;
        public int dmDitherType;
        public int dmReserved1;
        public int dmReserved2;
        public int dmPanningWidth;
        public int dmPanningHeight;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplaySettings(string? deviceName, int modeNum, ref DevMode devMode);

    /// <summary>Primary monitor refresh rate, kept between 60 and 240; 60 when unknown.</summary>
    public static int RefreshRate()
    {
        try
        {
            var mode = new DevMode { dmSize = (short)Marshal.SizeOf<DevMode>() };
            if (EnumDisplaySettings(null, EnumCurrentSettings, ref mode) && mode.dmDisplayFrequency > 1)
            {
                return Math.Clamp(mode.dmDisplayFrequency, 60, 240);
            }
        }
        catch
        {
            // Unknown display: the WPF default stays.
        }
        return 60;
    }

    /// <summary>Must run before the first animation is created, i.e. before any window loads.</summary>
    public static void UseMonitorFrameRate()
    {
        var rate = RefreshRate();
        if (rate > 60)
        {
            Timeline.DesiredFrameRateProperty.OverrideMetadata(typeof(Timeline),
                new FrameworkPropertyMetadata { DefaultValue = rate });
        }
    }
}
