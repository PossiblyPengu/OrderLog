using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SOUP.Converters;

/// <summary>
/// Converts string values to Visibility - visible when string has content, collapsed when empty/null
/// </summary>
public class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool invert = parameter?.ToString() == "Invert";
        bool hasContent = !string.IsNullOrWhiteSpace(value as string);

        if (invert)
        {
            return hasContent ? Visibility.Collapsed : Visibility.Visible;
        }
        else
        {
            return hasContent ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return Binding.DoNothing;
    }
}
