// Video Serial Visualizer - Copyright (C) 2026  David Nieves
// SPDX-License-Identifier: GPL-3.0-or-later
// Software libre, sin garantia alguna. Ver LICENSE para los terminos completos.

using System.Windows;
using System.Windows.Input;

namespace VideoSerialVisualizer.Helpers;

/// <summary>
/// Convierte cualquier elemento en una barra de arrastre horizontal (seek, volumen). Reemplaza al
/// Slider de WPF, que con plantilla personalizada es poco confiable: sus RepeatButton interceptan
/// clics de forma intermitente ("a veces salta, a veces no") y el binding TwoWay pelea con las
/// actualizaciones externas (el "rebote" de la perilla).
///
/// Aca el gesto se maneja directo: al presionar se captura el mouse y se calcula la fraccion 0..1
/// segun la X del cursor sobre el ancho del elemento. Un clic simple salta EXACTO al punto; arrastrar
/// actualiza en continuo. Los comandos Begin/Update/End reciben esa fraccion como parametro (double).
///
/// Update se dispara tambien al presionar y al soltar, asi un elemento que solo necesita "fijar el
/// valor" (p.ej. volumen) puede cablear unicamente UpdateCommand.
/// </summary>
public static class ScrubBar
{
    public static readonly DependencyProperty BeginCommandProperty = DependencyProperty.RegisterAttached(
        "BeginCommand", typeof(ICommand), typeof(ScrubBar));

    public static readonly DependencyProperty UpdateCommandProperty = DependencyProperty.RegisterAttached(
        "UpdateCommand", typeof(ICommand), typeof(ScrubBar));

    public static readonly DependencyProperty EndCommandProperty = DependencyProperty.RegisterAttached(
        "EndCommand", typeof(ICommand), typeof(ScrubBar));

    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(ScrubBar), new PropertyMetadata(false, OnIsEnabledChanged));

    public static void SetBeginCommand(DependencyObject o, ICommand v) => o.SetValue(BeginCommandProperty, v);
    public static ICommand? GetBeginCommand(DependencyObject o) => (ICommand?)o.GetValue(BeginCommandProperty);
    public static void SetUpdateCommand(DependencyObject o, ICommand v) => o.SetValue(UpdateCommandProperty, v);
    public static ICommand? GetUpdateCommand(DependencyObject o) => (ICommand?)o.GetValue(UpdateCommandProperty);
    public static void SetEndCommand(DependencyObject o, ICommand v) => o.SetValue(EndCommandProperty, v);
    public static ICommand? GetEndCommand(DependencyObject o) => (ICommand?)o.GetValue(EndCommandProperty);
    public static void SetIsEnabled(DependencyObject o, bool v) => o.SetValue(IsEnabledProperty, v);
    public static bool GetIsEnabled(DependencyObject o) => (bool)o.GetValue(IsEnabledProperty);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element)
            return;

        if ((bool)e.NewValue)
        {
            element.MouseLeftButtonDown += OnMouseDown;
            element.MouseMove += OnMouseMove;
            element.MouseLeftButtonUp += OnMouseUp;
        }
        else
        {
            element.MouseLeftButtonDown -= OnMouseDown;
            element.MouseMove -= OnMouseMove;
            element.MouseLeftButtonUp -= OnMouseUp;
        }
    }

    private static void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        var element = (FrameworkElement)sender;
        element.CaptureMouse();

        var fraction = FractionAt(element, e);
        Fire(GetBeginCommand(element), fraction);
        Fire(GetUpdateCommand(element), fraction);
        e.Handled = true;
    }

    private static void OnMouseMove(object sender, MouseEventArgs e)
    {
        var element = (FrameworkElement)sender;
        if (!element.IsMouseCaptured)
            return;

        Fire(GetUpdateCommand(element), FractionAt(element, e));
    }

    private static void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        var element = (FrameworkElement)sender;
        if (!element.IsMouseCaptured)
            return;

        element.ReleaseMouseCapture();

        var fraction = FractionAt(element, e);
        Fire(GetUpdateCommand(element), fraction);
        Fire(GetEndCommand(element), fraction);
        e.Handled = true;
    }

    private static double FractionAt(FrameworkElement element, MouseEventArgs e)
    {
        var width = element.ActualWidth;
        if (width <= 0)
            return 0;

        var x = e.GetPosition(element).X;
        return Math.Clamp(x / width, 0, 1);
    }

    private static void Fire(ICommand? command, double fraction)
    {
        if (command is not null && command.CanExecute(fraction))
            command.Execute(fraction);
    }
}
