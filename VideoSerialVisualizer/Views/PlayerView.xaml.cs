// Video Serial Visualizer - Copyright (C) 2026  David Nieves
// SPDX-License-Identifier: GPL-3.0-or-later
// Software libre, sin garantia alguna. Ver LICENSE para los terminos completos.

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using VideoSerialVisualizer.ViewModels;

namespace VideoSerialVisualizer.Views;

/// <remarks>
/// El video es contenido WPF comun (ver PlayerView.xaml). Antes vivia en una ventana nativa de
/// LibVLC y esta vista cargaba con los parches que eso exigia: el boton de play en un Popup que habia
/// que reposicionar a mano y marcar como no-activable, suspender el video durante el redimensionado
/// y recuperar el foco de teclado que la ventana nativa robaba al reproducir o al clickearla. Con el
/// video como contenido WPF nada de eso hace falta.
/// </remarks>
public partial class PlayerView : UserControl
{
    public PlayerView()
    {
        InitializeComponent();

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private Window? _hostWindow;

    // Se guarda al cargar: cuando WPF dispara Unloaded la vista ya esta desconectada del arbol y su
    // DataContext heredado es null (o el del siguiente view), asi que ahi no se puede confiar en el.
    private PlayerViewModel? _vm;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _vm = DataContext as PlayerViewModel;
        _hostWindow = Window.GetWindow(this);

        // Los atajos de teclado (KeyBinding, ver PlayerView.xaml) solo disparan mientras el foco de
        // teclado de WPF esta en este control o un descendiente WPF. Se reclama apenas se carga la
        // vista para que funcionen sin necesidad de un clic previo.
        Keyboard.Focus(this);
    }

    // --- Pantalla completa ---
    // Se guardan el estilo/estado previos de la ventana para poder volver exactamente a como estaba.
    private WindowStyle _prevWindowStyle;
    private ResizeMode _prevResizeMode;
    private WindowState _prevWindowState;

    private void ToggleFullScreen_Click(object sender, RoutedEventArgs e) => ToggleFullScreen();

    // Muestran/ocultan la barra de controles en pantalla completa (ver ShowControlsBar en el
    // ViewModel). Sin efecto en ventana normal, donde la barra siempre esta visible.
    private void ControlsBar_MouseEnter(object sender, MouseEventArgs e)
    {
        if (DataContext is PlayerViewModel vm)
            vm.IsControlsBarHovered = true;
    }

    private void ControlsBar_MouseLeave(object sender, MouseEventArgs e)
    {
        if (DataContext is PlayerViewModel vm)
            vm.IsControlsBarHovered = false;
    }

    private void ToggleFullScreen()
    {
        if (_hostWindow is null || DataContext is not PlayerViewModel vm)
            return;

        if (!vm.IsFullScreen)
        {
            _prevWindowStyle = _hostWindow.WindowStyle;
            _prevResizeMode = _hostWindow.ResizeMode;
            _prevWindowState = _hostWindow.WindowState;

            // WindowStyle=None + Maximized es la tecnica estandar de WPF para cubrir toda la pantalla
            // (incluida la barra de tareas). Se pasa por Normal primero para que el maximizado se
            // recalcule ya sin borde y tape la barra de tareas aunque ya estuviera maximizada.
            _hostWindow.WindowStyle = WindowStyle.None;
            _hostWindow.ResizeMode = ResizeMode.NoResize;
            _hostWindow.WindowState = WindowState.Normal;
            _hostWindow.WindowState = WindowState.Maximized;

            vm.IsFullScreen = true;
        }
        else
        {
            _hostWindow.WindowStyle = _prevWindowStyle;
            _hostWindow.ResizeMode = _prevResizeMode;
            _hostWindow.WindowState = _prevWindowState;

            vm.IsFullScreen = false;
        }

        // El foco vuelve a la vista para que los atajos (F, Esc, etc.) sigan disparando.
        Keyboard.Focus(this);
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not PlayerViewModel vm)
            return;

        // F alterna pantalla completa. Esc SOLO sale de pantalla completa (si no, se deja pasar para
        // que el atajo de limpiar segmento del modo animador siga funcionando).
        if (e.Key == Key.F)
        {
            ToggleFullScreen();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && vm.IsFullScreen)
        {
            ToggleFullScreen();
            e.Handled = true;
        }
    }

    // Volver atras estando en pantalla completa: la ventana se restaura YA, en el clic. El comando
    // Back es asincrono (guarda posicion, detiene el video) y recien al terminar cambia de vista;
    // si la restauracion esperara a Unloaded, la ventana seguiria a pantalla completa todo ese rato.
    // Click se dispara antes que el Command del boton, asi que corre primero.
    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        if (_vm is { IsFullScreen: true })
            ToggleFullScreen();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        var vm = _vm;
        _vm = null;

        // Red de seguridad para cualquier otra forma de salir del reproductor en pantalla completa
        // (error de reproduccion, siguiente video sin mas items...): se restaura la ventana para no
        // dejar la biblioteca/explorar en modo borderless.
        if (_hostWindow is not null && vm is { IsFullScreen: true })
        {
            _hostWindow.WindowStyle = _prevWindowStyle;
            _hostWindow.ResizeMode = _prevResizeMode;
            _hostWindow.WindowState = _prevWindowState;
            vm.IsFullScreen = false;
        }

        _hostWindow = null;
    }

    // Clic sobre el video (en vivo, el cuadro congelado del paso a cuadro o las bandas negras):
    // alterna play/pausa, que ademas sale del modo paso a cuadro.
    private void VideoArea_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is PlayerViewModel vm && vm.PlayPauseCommand.CanExecute(null))
            vm.PlayPauseCommand.Execute(null);

        Keyboard.Focus(this);
    }
}
