using System.Drawing;
using System.Windows.Media.Imaging;

namespace v2rayN.Manager;

public sealed class WindowsManager
{
    private static readonly Lazy<WindowsManager> instance = new(() => new());
    public static WindowsManager Instance => instance.Value;
    private static readonly string _tag = "WindowsHandler";

    public async Task<Icon> GetNotifyIcon(Config config)
    {
        return await GetNotifyIcon(ESvoRayConnectionState.Off);
    }

    /// <summary>
    /// Returns the tray icon for a connection state. The four icons differ by shape,
    /// not only by colour, so they stay readable at the 16-24 px system tray size.
    /// </summary>
    public async Task<Icon> GetNotifyIcon(ESvoRayConnectionState state)
    {
        try
        {
            await Task.CompletedTask;
            return LoadIcon(state switch
            {
                ESvoRayConnectionState.Connecting => "TrayConnecting.ico",
                ESvoRayConnectionState.On => "TrayOn.ico",
                ESvoRayConnectionState.Error => "TrayError.ico",
                _ => "TrayOff.ico"
            });
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
            return Properties.Resources.NotifyIcon1;
        }
    }

    public static string GetNotifyToolTip(ESvoRayConnectionState state)
    {
        return state switch
        {
            ESvoRayConnectionState.Connecting => ResUI.SvoRayTrayConnecting,
            ESvoRayConnectionState.On => ResUI.SvoRayTrayOn,
            ESvoRayConnectionState.Error => ResUI.SvoRayTrayError,
            _ => ResUI.SvoRayTrayOff
        };
    }

    public System.Windows.Media.ImageSource GetAppIcon(Config config)
    {
        return BitmapFrame.Create(new Uri("pack://application:,,,/Resources/SvoRay.ico", UriKind.RelativeOrAbsolute));
    }

    private static Icon LoadIcon(string resourceName)
    {
        var resource = Application.GetResourceStream(
            new Uri($"pack://application:,,,/Resources/{resourceName}", UriKind.RelativeOrAbsolute));
        if (resource?.Stream is null)
        {
            return Properties.Resources.NotifyIcon1;
        }

        // Pick the entry matching the shell's small-icon size instead of the 32 px
        // default, so the tray does not downscale a larger frame.
        var size = new System.Drawing.Size(
            (int)Math.Round(SystemParameters.SmallIconWidth),
            (int)Math.Round(SystemParameters.SmallIconHeight));

        using var icon = new Icon(resource.Stream, size);
        return (Icon)icon.Clone();
    }

    private async Task<Icon?> GetNotifyIcon4Routing(Config config)
    {
        try
        {
            var item = await ConfigHandler.GetDefaultRouting(config);
            if (item == null || item.CustomIcon.IsNullOrEmpty() || !File.Exists(item.CustomIcon))
            {
                return null;
            }

            var color = ColorTranslator.FromHtml("#3399CC");
            var index = (int)config.SystemProxyItem.SysProxyType;
            if (index > 0)
            {
                color = (new[] { Color.Red, Color.Purple, Color.DarkGreen, Color.Orange, Color.DarkSlateBlue, Color.RoyalBlue })[index - 1];
            }

            var width = 128;
            var height = 128;

            Bitmap bitmap = new(width, height);
            var graphics = Graphics.FromImage(bitmap);
            SolidBrush drawBrush = new(color);

            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            //graphics.FillRectangle(drawBrush, new Rectangle(0, 0, width, height));
            graphics.DrawImage(new Bitmap(item.CustomIcon), 0, 0, width, height);
            graphics.FillEllipse(drawBrush, width / 2, width / 2, width / 2, width / 2);

            var createdIcon = Icon.FromHandle(bitmap.GetHicon());

            drawBrush.Dispose();
            graphics.Dispose();
            bitmap.Dispose();

            return createdIcon;
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
            return null;
        }
    }

    public void RegisterGlobalHotkey(Config config, Action<EGlobalHotkey> handler, Action<bool, string>? update)
    {
        HotkeyManager.Instance.UpdateViewEvent += update;
        HotkeyManager.Instance.HotkeyTriggerEvent += handler;
        HotkeyManager.Instance.Load();
    }
}
