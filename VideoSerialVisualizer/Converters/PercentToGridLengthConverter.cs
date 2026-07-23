// Video Serial Visualizer - Copyright (C) 2026  David Nieves
// SPDX-License-Identifier: GPL-3.0-or-later
// Software libre, sin garantia alguna. Ver LICENSE para los terminos completos.

using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace VideoSerialVisualizer.Converters;

/// <summary>
/// Convierte un porcentaje (0-100) en un GridLength en unidades Star, para repartir
/// dos columnas proporcionalmente al progreso (p.ej. la barra de reproduccion).
/// ConverterParameter="Inverse" devuelve el complemento (100 - percent).
/// </summary>
public class PercentToGridLengthConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var percent = value is IConvertible ? System.Convert.ToDouble(value, culture) : 0;
        var inverse = string.Equals(parameter as string, "Inverse", StringComparison.OrdinalIgnoreCase);
        var factor = inverse ? 100 - percent : percent;
        return new GridLength(Math.Clamp(factor, 0, 100), GridUnitType.Star);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
