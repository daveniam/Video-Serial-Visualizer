// Video Serial Visualizer - Copyright (C) 2026  David Nieves
// SPDX-License-Identifier: GPL-3.0-or-later
// Software libre, sin garantia alguna. Ver LICENSE para los terminos completos.

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
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
    private HwndSource? _hostSource;
    private bool _inSizeMove;

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
            // El overlay de play vive en su propia ventana (Popup): no sigue sola a la principal, hay
            // que reposicionarla a mano cuando esta se mueve, cambia de tamano o se restaura.
            _hostWindow.LocationChanged += OnHostWindowMovedOrResized;
            _hostWindow.SizeChanged += OnHostWindowMovedOrResized;
            _hostWindow.StateChanged += OnHostWindowStateChanged;
            if (DataContext is PlayerViewModel v)
                v.IsWindowActive = _hostWindow.IsActive;

            // Se escucha el ciclo modal de redimensionado (WM_ENTER/EXITSIZEMOVE + WM_SIZING) para
            // suspender la superficie nativa del video mientras se arrastra el borde. Ver HostWndProc.
            _hostSource = PresentationSource.FromVisual(_hostWindow) as HwndSource;
            _hostSource?.AddHook(HostWndProc);
        }

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

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is PlayerViewModel vm)
            vm.DetachVideoSurface();

        // Al salir del reproductor estando en pantalla completa, se restaura la ventana para no dejar
        // la biblioteca/explorar en modo borderless.
        if (_hostWindow is not null && DataContext is PlayerViewModel v && v.IsFullScreen)
        {
            _hostWindow.WindowStyle = _prevWindowStyle;
            _hostWindow.ResizeMode = _prevResizeMode;
            _hostWindow.WindowState = _prevWindowState;
            v.IsFullScreen = false;
        }

        if (_hostWindow is not null)
        {
            _hostWindow.Activated -= OnHostWindowActivated;
            _hostWindow.Deactivated -= OnHostWindowDeactivated;
            _hostWindow.LocationChanged -= OnHostWindowMovedOrResized;
            _hostWindow.SizeChanged -= OnHostWindowMovedOrResized;
            _hostWindow.StateChanged -= OnHostWindowStateChanged;
            _hostWindow = null;
        }

        _hostSource?.RemoveHook(HostWndProc);
        _hostSource = null;

        // Por las dudas: no dejar la superficie suspendida si se sale del reproductor a mitad de un
        // redimensionado.
        _inSizeMove = false;
        if (DataContext is PlayerViewModel rv)
            rv.IsResizingWindow = false;
    }

    private void OnHostWindowActivated(object? sender, EventArgs e)
    {
        if (DataContext is not PlayerViewModel vm)
            return;

        // Se DIFIERE reabrir el overlay hasta que la activacion/restauracion de la ventana termine.
        // Reabrirlo sincronicamente dentro del Activated (al restaurar desde minimizado) hace que el
        // Popup pelee con la restauracion: un pitido y un segundo clic. Posponerlo a prioridad baja
        // deja que Windows traiga la ventana al frente primero y recien ahi aparece el boton.
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (_hostWindow is not null && _hostWindow.WindowState != WindowState.Minimized)
                vm.IsWindowActive = true;
        }), System.Windows.Threading.DispatcherPriority.Background);
    }

    private void OnHostWindowDeactivated(object? sender, EventArgs e)
    {
        if (DataContext is PlayerViewModel vm)
            vm.IsWindowActive = false;
    }

    private void OnHostWindowMovedOrResized(object? sender, EventArgs e) => RepositionPlayOverlay();

    /// <summary>
    /// Suspende la superficie nativa del video durante el arrastre de redimensionado. Con la GPU
    /// apagada, reescalar cada cuadro por CPU y reubicar la ventana nativa del WindowsFormsHost en
    /// cada pixel del arrastre es lo que traba. Se detecta el ciclo modal (WM_ENTERSIZEMOVE) y, solo
    /// si es un REDIMENSIONADO (WM_SIZING, no un simple mover), se colapsa la superficie (queda el
    /// fondo negro); al soltar (WM_EXITSIZEMOVE) se restaura ya con el tamano final. El audio no se
    /// corta: VLC sigue decodificando, solo no se muestra mientras se arrastra.
    /// </summary>
    private IntPtr HostWndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_SIZING = 0x0214;
        const int WM_ENTERSIZEMOVE = 0x0231;
        const int WM_EXITSIZEMOVE = 0x0232;

        if (DataContext is not PlayerViewModel vm)
            return IntPtr.Zero;

        switch (msg)
        {
            case WM_ENTERSIZEMOVE:
                _inSizeMove = true;
                break;

            case WM_SIZING:
                if (_inSizeMove && !vm.IsResizingWindow)
                    vm.IsResizingWindow = true;
                break;

            case WM_EXITSIZEMOVE:
                _inSizeMove = false;
                if (vm.IsResizingWindow)
                    vm.IsResizingWindow = false;
                break;
        }

        return IntPtr.Zero;
    }

    /// <summary>
    /// Al minimizar hay que CERRAR el overlay de play (Popup) de forma explicita. Es una ventana
    /// topmost propia y, si queda abierta con la principal minimizada, bloquea la restauracion: al
    /// clickear la barra de tareas Windows no puede traer la principal al frente (suena el pitido y
    /// hacen falta varios clics). Cerrarlo la deja restaurar limpio de un solo clic; al volver
    /// (Activated) se reabre si el video sigue pausado.
    /// </summary>
    private void OnHostWindowStateChanged(object? sender, EventArgs e)
    {
        if (_hostWindow is not null && _hostWindow.WindowState == WindowState.Minimized
            && DataContext is PlayerViewModel vm)
        {
            vm.IsWindowActive = false;
        }

        RepositionPlayOverlay();
    }

    /// <summary>Fuerza al Popup del overlay a recalcular su posicion respecto del video. WPF no lo
    /// hace solo cuando la ventana se mueve: se le da un empujoncito al offset (ida y vuelta) para
    /// que se recoloque centrado sobre el video en su nueva ubicacion.</summary>
    private void RepositionPlayOverlay()
    {
        if (!PlayOverlayPopup.IsOpen)
            return;

        var offset = PlayOverlayPopup.HorizontalOffset;
        PlayOverlayPopup.HorizontalOffset = offset + 1;
        PlayOverlayPopup.HorizontalOffset = offset;
    }

    /// <summary>Al abrirse el Popup se marca su ventana Win32 como no activable, asi no roba la
    /// activacion de la ventana principal (evita el pitido y el doble clic al restaurar).</summary>
    private void PlayOverlayPopup_Opened(object? sender, EventArgs e)
    {
        if (PresentationSource.FromVisual(PlayOverlayPopup.Child) is HwndSource source)
            WindowEffectsHelper.SetNoActivate(source.Handle);
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
