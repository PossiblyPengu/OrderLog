using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace SOUP.Features.OrderLog.Converters;

public class HexToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string hex && !string.IsNullOrWhiteSpace(hex))
        {
            try
            {
                if (!hex.StartsWith("#"))
                    hex = "#" + hex;

                var color = (Color)ColorConverter.ConvertFromString(hex);
                return color;
            }
            catch
            {
                // Invalid color format, fall through to default
            }
        }
        // Fallback: Transparent
        return Colors.Transparent;
    }

    public object? ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is Color color)
            return color.ToString();
        return null;
    }
}
