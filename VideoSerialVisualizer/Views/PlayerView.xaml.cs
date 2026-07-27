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
        _videoPanel = new VideoSurfacePanel { BackColor = System.Drawing.Color.Black, TabStop = false };
        _videoPanel.VideoClicked += OnVideoClicked;
        VideoHost.Child = _videoPanel;

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private Window? _hostWindow;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Acceder a Handle fuerza la creacion de la ventana nativa; se la pasamos al MediaPlayer
        // ANTES de reproducir (MainViewModel espera a este Loaded), asi LibVLC pinta aca dentro.
        if (DataContext is PlayerViewModel vm)
            vm.AttachVideoSurface(_videoPanel.Handle);

        // Se sigue si la ventana esta activa: el overlay de play (Popup) se oculta cuando no lo
        // esta, para que no quede flotando encima de otras aplicaciones.
        _hostWindow = Window.GetWindow(this);
        if (_hostWindow is not null)
        {
            _hostWindow.Activated += OnHostWindowActivated;
            _hostWindow.Deactivated += OnHostWindowDeactivated;
            if (DataContext is PlayerViewModel v)
                v.IsWindowActive = _hostWindow.IsActive;
        }

        // Los atajos de teclado (KeyBinding, ver PlayerView.xaml) solo disparan mientras el foco de
        // teclado de WPF esta en este control o un descendiente WPF. Se reclama apenas se carga la
        // vista para que funcionen sin necesidad de un clic previo.
        Keyboard.Focus(this);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is PlayerViewModel vm)
            vm.DetachVideoSurface();

        if (_hostWindow is not null)
        {
            _hostWindow.Activated -= OnHostWindowActivated;
            _hostWindow.Deactivated -= OnHostWindowDeactivated;
            _hostWindow = null;
        }
    }

    private void OnHostWindowActivated(object? sender, EventArgs e)
    {
        if (DataContext is PlayerViewModel vm)
            vm.IsWindowActive = true;
    }

    private void OnHostWindowDeactivated(object? sender, EventArgs e)
    {
        if (DataContext is PlayerViewModel vm)
            vm.IsWindowActive = false;
    }

    // El clic llega desde el hilo de mensajes del panel nativo; se marshalea al hilo de UI.
    private void OnVideoClicked(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (DataContext is PlayerViewModel vm && vm.PlayPauseCommand.CanExecute(null))
                vm.PlayPauseCommand.Execute(null);

            // Un clic nativo le da el foco de Windows a la ventana hija de LibVLC; sin esto, los
            // KeyBinding de WPF (space, I/O, paso a cuadro) dejan de recibir teclas hasta el
            // proximo Tab.
            Keyboard.Focus(this);
        });
    }

    // Clic sobre el cuadro congelado del modo paso a cuadro: mismo gesto que clickear el video en
    // reproduccion normal (alternar play/pausa, que ademas sale del modo paso a cuadro).
    private void FrameStepImage_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is PlayerViewModel vm && vm.PlayPauseCommand.CanExecute(null))
            vm.PlayPauseCommand.Execute(null);

        Keyboard.Focus(this);
    }

}
