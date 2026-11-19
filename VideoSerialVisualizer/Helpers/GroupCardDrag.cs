// Video Serial Visualizer - Copyright (C) 2026  David Nieves
// SPDX-License-Identifier: GPL-3.0-or-later
// Software libre, sin garantia alguna. Ver LICENSE para los terminos completos.

using System.Windows;
using System.Windows.Input;
using VideoSerialVisualizer.ViewModels;

namespace VideoSerialVisualizer.Helpers;

/// <summary>
/// Arrastrar una tarjeta de grupo (a un curso en Explorar, o para reordenar dentro de un curso).
///
/// La tarjeta entera es un boton (clic = abrir el grupo), asi que el arrastre se detecta con los
/// eventos Preview, que pasan por la tarjeta aunque el boton tenga capturado el mouse. Recien cuando
/// el mouse se aleja lo suficiente con el boton apretado empieza el arrastre; DoDragDrop le quita la
/// captura al boton, asi que soltar no dispara tambien el clic.
/// </summary>
public static class GroupCardDrag
{
    public const string DataFormat = "VideoSerialVisualizer.GroupCard";

    private static Point? _pressPoint;
    private static FrameworkElement? _pressSource;

    public static void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _pressSource = sender as FrameworkElement;
        _pressPoint = e.GetPosition(null);
    }

    public static void OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _pressPoint is not Point start || !ReferenceEquals(sender, _pressSource))
            return;

        var delta = e.GetPosition(null) - start;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance * 2
            && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance * 2)
            return;

        if (_pressSource?.DataContext is not FolderCardViewModel card)
            return;

        var source = _pressSource;
        _pressPoint = null;
        _pressSource = null;

        var data = new DataObject(DataFormat, card);
        source.Opacity = 0.5;
        try
        {
            DragDrop.DoDragDrop(source, data, DragDropEffects.Move);
        }
        finally
        {
            source.Opacity = 1;
        }
    }

    /// <summary>El grupo que se esta arrastrando, si lo que viene es una tarjeta de grupo.</summary>
    public static FolderCardViewModel? GetDragged(DragEventArgs e)
        => e.Data.GetDataPresent(DataFormat) ? e.Data.GetData(DataFormat) as FolderCardViewModel : null;
}
