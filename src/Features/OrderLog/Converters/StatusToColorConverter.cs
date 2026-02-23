using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using SOUP.Features.OrderLog.Models;

namespace SOUP.Features.OrderLog.Converters;

public static class OrderLogColors
{
    // Predefined brushes used across OrderLog views
    private static readonly Brush NotReadyBrush = new SolidColorBrush(Color.FromRgb(0x18, 0x18, 0x1b));    // #18181b zinc-900
    private static readonly Brush OnDeckBrush = new SolidColorBrush(Color.FromRgb(0x1c, 0x1c, 0x22));      // #1c1c22 slightly lighter
    private static readonly Brush InProgressBrush = new SolidColorBrush(Color.FromRgb(0x14, 0x53, 0x2d));  // #14532d green-900
    private static readonly Brush DoneBrush = new SolidColorBrush(Color.FromRgb(0x27, 0x27, 0x2a));        // #27272a zinc-800

    public static Brush FromStatus(OrderItem.OrderStatus status)
    {
        return status switch
        {
            OrderItem.OrderStatus.NotReady => NotReadyBrush,
            OrderItem.OrderStatus.OnDeck => OnDeckBrush,
            OrderItem.OrderStatus.InProgress => InProgressBrush,
            OrderItem.OrderStatus.Done => DoneBrush,
            _ => NotReadyBrush,
        };
    }
}

/// <summary>
/// Converts an OrderItem.OrderStatus to a theme-aware brush or Color.
/// Uses application resources when available, with sensible fallbacks.
/// </summary>
public class StatusToColorConverter : IValueConverter
{
    private static Brush? _dangerBrush;
    private static Brush? _warningBrush;
    private static Brush? _successBrush;
    private static Brush? _tertiaryBrush;
    private static volatile bool _cacheInitialized;

    private static void EnsureCacheInitialized()
    {
        if (_cacheInitialized) return;
        var app = Application.Current;
        _dangerBrush = app?.TryFindResource("DangerBrush") as Brush;
        _warningBrush = app?.TryFindResource("WarningBrush") as Brush;
        _successBrush = app?.TryFindResource("SuccessBrush") as Brush;
        _tertiaryBrush = app?.TryFindResource("TextTertiaryBrush") as Brush;
        _cacheInitialized = true;
    }

    /// <summary>Clears cached brushes (call on theme change)</summary>
    public static void InvalidateCache() => _cacheInitialized = false;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not OrderItem.OrderStatus status)
            return DependencyProperty.UnsetValue;

        EnsureCacheInitialized();

        var brush = status switch
        {
            OrderItem.OrderStatus.NotReady => _dangerBrush,
            OrderItem.OrderStatus.OnDeck => _warningBrush,
            OrderItem.OrderStatus.InProgress => _successBrush,
            OrderItem.OrderStatus.Done => _tertiaryBrush,
            _ => _dangerBrush
        };

        if (brush != null)
        {
            return targetType == typeof(Color) && brush is SolidColorBrush solidBrush
                ? solidBrush.Color
                : brush;
        }

        // Fallback colors if resources not found
        return status switch
        {
            OrderItem.OrderStatus.NotReady => targetType == typeof(Color) ? Colors.Red : Brushes.Red,
            OrderItem.OrderStatus.OnDeck => targetType == typeof(Color) ? Colors.Orange : Brushes.Orange,
            OrderItem.OrderStatus.InProgress => targetType == typeof(Color) ? Colors.Green : Brushes.Green,
            OrderItem.OrderStatus.Done => targetType == typeof(Color) ? Colors.Gray : Brushes.Gray,
            _ => targetType == typeof(Color) ? Colors.Red : Brushes.Red
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return Binding.DoNothing;
    }
}

/// <summary>
/// Converts OrderItem.OrderStatus to a card background brush.
/// Uses cached brushes that match the theme colors for card backgrounds.
/// </summary>
public class StatusToCardBrushConverter : IValueConverter
{
    // Cache brushes - dark theme card colors
    private static readonly Brush NotReadyBrush = new SolidColorBrush(Color.FromRgb(0x18, 0x18, 0x1b));    // #18181b zinc-900
    private static readonly Brush OnDeckBrush = new SolidColorBrush(Color.FromRgb(0x1c, 0x1c, 0x22));      // #1c1c22 slightly lighter
    private static readonly Brush InProgressBrush = new SolidColorBrush(Color.FromRgb(0x14, 0x53, 0x2d));  // #14532d green-900
    private static readonly Brush DoneBrush = new SolidColorBrush(Color.FromRgb(0x27, 0x27, 0x2a));        // #27272a zinc-800

    static StatusToCardBrushConverter()
    {
        // Freeze brushes for performance
        NotReadyBrush.Freeze();
        OnDeckBrush.Freeze();
        InProgressBrush.Freeze();
        DoneBrush.Freeze();
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not OrderItem.OrderStatus status)
            return DependencyProperty.UnsetValue;

        return status switch
        {
            OrderItem.OrderStatus.NotReady => NotReadyBrush,
            OrderItem.OrderStatus.OnDeck => OnDeckBrush,
            OrderItem.OrderStatus.InProgress => InProgressBrush,
            OrderItem.OrderStatus.Done => DoneBrush,
            _ => NotReadyBrush
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return Binding.DoNothing;
    }
}
