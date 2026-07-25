// Video Serial Visualizer - Copyright (C) 2026  David Nieves
// SPDX-License-Identifier: GPL-3.0-or-later
// Software libre, sin garantia alguna. Ver LICENSE para los terminos completos.

using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace VideoSerialVisualizer.Converters;

/// <summary>
/// Pinta de amarillo la marca de cuadro que coincide con el inicio o el fin del segmento marcado.
/// Valores esperados: [0] el indice de ESTA marca (el item del ItemsControl), [1] SegmentStartTickIndex,
/// [2] SegmentEndTickIndex (los dos ultimos, nullable, vienen del ViewModel via AncestorType=ItemsControl).
/// </summary>
public class TickHighlightConverter : IMultiValueConverter
{
    private static readonly SolidColorBrush MarkerBrush = new((Color)ColorConverter.ConvertFromString("#FFD600"));

    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Length < 3 || values[0] is not int index)
            return Brushes.Transparent;

        var isStart = values[1] is int start && start == index;
        var isEnd = values[2] is int end && end == index;

        return isStart || isEnd ? MarkerBrush : Brushes.Transparent;
    }

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
