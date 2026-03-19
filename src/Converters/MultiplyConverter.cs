using System;
using System.Globalization;
using System.Windows.Data;

namespace OrderLog.Converters;

/// <summary>
/// Multiplies a numeric value by the supplied ConverterParameter (double) to drive responsive sizing.
/// </summary>
public class MultiplyConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is double numericValue && parameter != null && double.TryParse(parameter.ToString(), out double factor))
        {
            return numericValue * factor;
        }

        return Binding.DoNothing;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
