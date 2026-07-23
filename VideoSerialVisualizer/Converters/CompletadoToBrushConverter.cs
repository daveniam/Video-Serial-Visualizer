// Video Serial Visualizer - Copyright (C) 2026  David Nieves
// SPDX-License-Identifier: GPL-3.0-or-later
// Software libre, sin garantia alguna. Ver LICENSE para los terminos completos.

using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace VideoSerialVisualizer.Converters;

public class CompletadoToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush CompletadoBrush = new(Color.FromRgb(0x2E, 0xA0, 0x4A));
    // Mismo azul de acento que el resto de la app (#1CA1D0); el verde marca "completado".
    private static readonly SolidColorBrush EnProgresoBrush = new(Color.FromRgb(0x1C, 0xA1, 0xD0));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? CompletadoBrush : EnProgresoBrush;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
