// Video Serial Visualizer - Copyright (C) 2026  David Nieves
// SPDX-License-Identifier: GPL-3.0-or-later
// Software libre, sin garantia alguna. Ver LICENSE para los terminos completos.

using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace VideoSerialVisualizer.Converters;

/// <summary>
/// ConverterParameter: "#ColorSiTrue;#ColorSiFalse"
/// </summary>
public class BoolToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var isTrue = value is true;
        var parts = (parameter as string)?.Split(';') ?? new[] { "#FFFFFF", "#000000" };
        var colorText = isTrue ? parts[0] : parts[Math.Min(1, parts.Length - 1)];
        return new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorText));
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
