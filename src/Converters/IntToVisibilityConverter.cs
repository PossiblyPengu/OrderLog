using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SOUP.Converters;

public class IntToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var invert = parameter is string p && p.Equals("Invert", StringComparison.OrdinalIgnoreCase);

        if (value is int intValue)
        {
            var visible = intValue > 0;
            if (invert) visible = !visible;
            return visible ? Visibility.Visible : Visibility.Collapsed;
        }
        return invert ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return Binding.DoNothing;
    }
}
