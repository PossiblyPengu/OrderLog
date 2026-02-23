using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SOUP.Features.OrderLog.Converters;

/// <summary>
/// Returns Visible when a Guid? is non-null and not Guid.Empty, otherwise Collapsed.
/// Used to show a link indicator on linked orders.
/// </summary>
public class LinkedGroupToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is Guid g && g != Guid.Empty) return Visibility.Visible;
        if (value is Guid?)
        {
            var gv = (Guid?)value;
            return (gv.HasValue && gv.Value != Guid.Empty) ? Visibility.Visible : Visibility.Collapsed;
        }

        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return Binding.DoNothing;
    }
}
