// Video Serial Visualizer - Copyright (C) 2026  David Nieves
// SPDX-License-Identifier: GPL-3.0-or-later
// Software libre, sin garantia alguna. Ver LICENSE para los terminos completos.

using System.Globalization;
using System.Windows.Data;

namespace VideoSerialVisualizer.Converters;

/// <summary>
/// Convierte (porcentaje 0-100, ancho disponible en pixeles) en la posicion Canvas.Left que le
/// corresponde. Se usa para ubicar los marcadores de la linea de tiempo, cuya posicion es arbitraria
/// (a diferencia de las marcas de cuadro, que se reparten en columnas iguales via UniformGrid).
/// </summary>
public class PercentToCanvasLeftConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Length < 2 || values[0] is not double percent || values[1] is not double width)
            return 0.0;

        return width * Math.Clamp(percent, 0, 100) / 100.0;
    }

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
