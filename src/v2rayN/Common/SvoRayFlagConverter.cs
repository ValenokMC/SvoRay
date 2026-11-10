using System.Collections.Concurrent;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace v2rayN.Common;

/// <summary>A country code ("de") to its flag picture from Resources\Flags; null when there is none.</summary>
public sealed class SvoRayFlagConverter : IValueConverter
{
    private static readonly ConcurrentDictionary<string, BitmapImage?> Cache = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is string { Length: 2 } code ? Cache.GetOrAdd(code.ToLowerInvariant(), Load) : null;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    private static BitmapImage? Load(string code)
    {
        try
        {
            var image = new BitmapImage(new Uri($"pack://application:,,,/Resources/Flags/{code}.png", UriKind.Absolute));
            image.Freeze();
            return image;
        }
        catch (Exception)
        {
            // No picture for this code (or an unreadable one): the row shows the name alone.
            return null;
        }
    }
}
