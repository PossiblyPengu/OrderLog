using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SOUP.Converters;

/// <summary>
/// Converts null values to Visibility
/// </summary>
public class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        // If parameter is "Invert", show when null, hide when not null
        bool invert = parameter?.ToString() == "Invert";

        bool isNull = value == null;

        if (invert)
        {
            return isNull ? Visibility.Visible : Visibility.Collapsed;
        }
        else
        {
            return isNull ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return Binding.DoNothing;
    }
}
