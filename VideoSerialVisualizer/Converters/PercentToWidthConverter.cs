using System.Globalization;
using System.Windows.Data;

namespace VideoSerialVisualizer.Converters;

public class PercentToWidthConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var percent = value is double d ? d : 0;
        var maxWidth = parameter is string s && double.TryParse(s, out var w) ? w : 200;
        return maxWidth * Math.Clamp(percent, 0, 100) / 100.0;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
