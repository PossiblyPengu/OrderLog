using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using SOUP.Features.OrderLog.Models;

namespace SOUP.Features.OrderLog.Converters;

/// <summary>
/// Multi-value converter that returns a highlight brush if the item is the link mode source.
/// Values[0]: The current item (OrderItem or OrderItemGroup)
/// Values[1]: The LinkModeSource (OrderItem?)
/// Values[2]: IsLinkMode (bool)
/// </summary>
public class IsLinkSourceToBrushConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 3) return DependencyProperty.UnsetValue;
        if (values[2] is not bool isLinkMode || !isLinkMode) return DependencyProperty.UnsetValue;
        if (values[1] is not OrderItem linkSource) return DependencyProperty.UnsetValue;

        OrderItem? currentItem = values[0] switch
        {
            OrderItem item => item,
            ViewModels.OrderItemGroup group => group.First,
            _ => null
        };

        if (currentItem == null) return DependencyProperty.UnsetValue;

        // Check if this is the source item
        if (currentItem.Id == linkSource.Id)
        {
            // Blue highlight for source
            return new SolidColorBrush(Color.FromRgb(59, 130, 246)); // Tailwind blue-500
        }

        return DependencyProperty.UnsetValue;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Multi-value converter that returns border thickness if the item is the link mode source.
/// </summary>
public class IsLinkSourceToThicknessConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 3) return new Thickness(0);
        if (values[2] is not bool isLinkMode || !isLinkMode) return new Thickness(0);
        if (values[1] is not OrderItem linkSource) return new Thickness(0);

        OrderItem? currentItem = values[0] switch
        {
            OrderItem item => item,
            ViewModels.OrderItemGroup group => group.First,
            _ => null
        };

        if (currentItem == null) return new Thickness(0);

        // Check if this is the source item
        if (currentItem.Id == linkSource.Id)
        {
            return new Thickness(2);
        }

        return new Thickness(0);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
