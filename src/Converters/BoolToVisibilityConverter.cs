using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SOUP.Converters;

/// <summary>
/// Converts a boolean value to <see cref="Visibility"/>.
/// </summary>
/// <remarks>
/// <c>true</c> converts to <see cref="Visibility.Visible"/>,
/// <c>false</c> converts to <see cref="Visibility.Collapsed"/>.
/// Use ConverterParameter="Invert" to reverse the logic.
/// </remarks>
public class BoolToVisibilityConverter : IValueConverter
{
    /// <inheritdoc/>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            // Check if we should invert
            bool invert = parameter is string s && s.Equals("Invert", StringComparison.OrdinalIgnoreCase);
            if (invert) boolValue = !boolValue;

            return boolValue ? Visibility.Visible : Visibility.Collapsed;
        }
        return Visibility.Collapsed;
    }

    /// <inheritdoc/>
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is Visibility visibility)
        {
            bool result = visibility == Visibility.Visible;
            bool invert = parameter is string s && s.Equals("Invert", StringComparison.OrdinalIgnoreCase);
            return invert ? !result : result;
        }
        return false;
    }
}

/// <summary>
/// Converts a boolean value to <see cref="Visibility"/> with inverse logic.
/// </summary>
/// <remarks>
/// <c>true</c> converts to <see cref="Visibility.Collapsed"/>,
/// <c>false</c> converts to <see cref="Visibility.Visible"/>.
/// </remarks>
public class InverseBoolToVisibilityConverter : IValueConverter
{
    /// <inheritdoc/>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            return boolValue ? Visibility.Collapsed : Visibility.Visible;
        }
        return Visibility.Visible;
    }

    /// <inheritdoc/>
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is Visibility visibility)
        {
            return visibility == Visibility.Collapsed;
        }
        return false;
    }
}

/// <summary>
/// Converts a boolean to a sort arrow character.
/// </summary>
/// <remarks>
/// <c>true</c> (descending) converts to "↓",
/// <c>false</c> (ascending) converts to "↑".
/// </remarks>
public class BoolToSortArrowConverter : IValueConverter
{
    /// <inheritdoc/>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool isDescending)
        {
            return isDescending ? "↓" : "↑";
        }
        return "↑";
    }

    /// <inheritdoc/>
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value?.ToString() == "↓";
    }
}

/// <summary>
/// Inverts a boolean value (true becomes false, false becomes true).
/// </summary>
public class InverseBoolConverter : IValueConverter
{
    /// <inheritdoc/>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            return !boolValue;
        }
        return true;
    }

    /// <inheritdoc/>
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            return !boolValue;
        }
        return false;
    }
}
