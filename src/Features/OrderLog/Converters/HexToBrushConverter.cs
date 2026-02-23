using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace SOUP.Features.OrderLog.Converters;

public class HexToBrushConverter : IValueConverter
{
    private static readonly Dictionary<string, SolidColorBrush> _brushCache = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string hex && !string.IsNullOrWhiteSpace(hex))
        {
            try
            {
                if (!hex.StartsWith("#"))
                    hex = "#" + hex;

                // Return cached brush if available
                if (_brushCache.TryGetValue(hex, out var cached))
                    return cached;

                var color = (Color)ColorConverter.ConvertFromString(hex);
                var brush = new SolidColorBrush(color);
                brush.Freeze(); // Improve performance and allow cross-thread access
                _brushCache[hex] = brush;
                return brush;
            }
            catch
            {
                // Invalid color format, fall through to default
            }
        }
        // Fallback: Transparent
        return Brushes.Transparent;
    }

    public object? ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is SolidColorBrush brush)
            return brush.Color.ToString();
        return null;
    }
}
