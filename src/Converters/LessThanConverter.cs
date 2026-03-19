using System;
using System.Globalization;
using System.Windows.Data;

namespace OrderLog.Converters;

/// <summary>
/// Returns true when the bound double value is less than the supplied ConverterParameter.
/// Useful for responsive layout triggers.
/// </summary>
public class LessThanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is double numericValue && parameter != null && double.TryParse(parameter.ToString(), out double threshold))
        {
            return numericValue < threshold;
        }
        return false;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
