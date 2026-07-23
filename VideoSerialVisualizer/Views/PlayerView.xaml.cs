// Video Serial Visualizer - Copyright (C) 2026  David Nieves
// SPDX-License-Identifier: GPL-3.0-or-later
// Software libre, sin garantia alguna. Ver LICENSE para los terminos completos.

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using VideoSerialVisualizer.Helpers;
using VideoSerialVisualizer.ViewModels;

namespace VideoSerialVisualizer.Views;

public partial class PlayerView : UserControl
{
    private readonly VideoSurfacePanel _videoPanel;

    public PlayerView()
    {
        InitializeComponent();

        // Panel nativo (WinForms) que sirve de superficie de render para LibVLC. Fondo negro
        // para que nunca se vea blanco/gris antes de que el video pinte.
        _videoPanel = new VideoSurfacePanel { BackColor = System.Drawing.Color.Black };
        _videoPanel.VideoClicked += OnVideoClicked;
        VideoHost.Child = _videoPanel;

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;

        SeekSlider.PreviewMouseLeftButtonDown += SeekSlider_PreviewMouseLeftButtonDown;
        SeekSlider.PreviewMouseLeftButtonUp += SeekSlider_PreviewMouseLeftButtonUp;
        SeekSlider.PreviewKeyDown += SeekSlider_PreviewKeyDown;
        SeekSlider.PreviewKeyUp += SeekSlider_PreviewKeyUp;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Acceder a Handle fuerza la creacion de la ventana nativa; se la pasamos al MediaPlayer
        // ANTES de reproducir (MainViewModel espera a este Loaded), asi LibVLC pinta aca dentro.
        if (DataContext is PlayerViewModel vm)
            vm.AttachVideoSurface(_videoPanel.Handle);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is PlayerViewModel vm)
            vm.DetachVideoSurface();
    }

    // El clic llega desde el hilo de mensajes del panel nativo; se marshalea al hilo de UI.
    private void OnVideoClicked(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (DataContext is PlayerViewModel vm && vm.PlayPauseCommand.CanExecute(null))
                vm.PlayPauseCommand.Execute(null);
        });
    }

    private void SeekSlider_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is PlayerViewModel vm)
            vm.BeginUserSeek();
    }

    private void SeekSlider_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is PlayerViewModel vm)
            vm.EndUserSeek(SeekSlider.Value);
    }

    private void SeekSlider_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is PlayerViewModel vm)
            vm.BeginUserSeek();
    }

    private void SeekSlider_PreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (DataContext is PlayerViewModel vm)
            vm.EndUserSeek(SeekSlider.Value);
    }
}
