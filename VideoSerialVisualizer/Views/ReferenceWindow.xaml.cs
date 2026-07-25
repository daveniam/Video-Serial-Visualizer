// Video Serial Visualizer - Copyright (C) 2026  David Nieves
// SPDX-License-Identifier: GPL-3.0-or-later
// Software libre, sin garantia alguna. Ver LICENSE para los terminos completos.

using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using VideoSerialVisualizer.Helpers;
using VideoSerialVisualizer.Localization;
using VideoSerialVisualizer.ViewModels;

namespace VideoSerialVisualizer.Views;

/// <summary>
/// Ventana de referencia flotante del modo animador: muestra el mismo video que el reproductor
/// (comparte su PlayerViewModel) en una ventana semitransparente que se puede dejar encima de otro
/// programa para calcar o comparar.
/// </summary>
public partial class ReferenceWindow : Window
{
    /// <summary>
    /// Combinacion para salir del modo click-through. Se eligio Ctrl+Alt+T por ser poco habitual en
    /// programas de dibujo y 3D, que es justamente donde va a estar el foco cuando haga falta.
    /// </summary>
    private const ModifierKeys ExitClickThroughModifiers = ModifierKeys.Control | ModifierKeys.Alt;
    private const Key ExitClickThroughKey = Key.T;

    public static string ExitClickThroughShortcutText => "Ctrl+Alt+T";

    private GlobalHotkey? _exitClickThroughHotkey;
    private PlayerViewModel? _viewModel;

    public ReferenceWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        SourceInitialized += OnSourceInitialized;
        Closed += OnClosed;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        // AllowsTransparency obliga a WindowStyle=None, y eso deja a la ventana SIN el marco no
        // cliente de Windows, que es la zona que normalmente se agarra para redimensionar: por eso
        // ResizeMode="CanResize" por si solo no alcanza. Se responde a mano el WM_NCHITTEST para
        // decirle a Windows que el cursor esta sobre un borde, y ahi el redimensionado nativo
        // (con sus cursores y su snap) funciona igual que en cualquier ventana.
        if (PresentationSource.FromVisual(this) is HwndSource source)
            source.AddHook(ResizeHitTestHook);
    }

    private IntPtr ResizeHitTestHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (WindowState != WindowState.Normal)
            return IntPtr.Zero;

        return msg switch
        {
            WM_NCHITTEST => HandleHitTest(hwnd, lParam, ref handled),
            WM_SIZING => HandleSizing(lParam, ref handled),
            _ => IntPtr.Zero,
        };
    }

    /// <summary>
    /// Se redimensiona UNICAMENTE desde la esquina inferior derecha, la que tiene la marca visible.
    /// Con un solo agarre desaparece la ambiguedad que habia al arrastrar la ventana: antes, los
    /// primeros pixeles de la barra superior redimensionaban en vez de mover.
    /// </summary>
    private IntPtr HandleHitTest(IntPtr hwnd, IntPtr lParam, ref bool handled)
    {
        if (!GetWindowRect(hwnd, out var rect))
            return IntPtr.Zero;

        // lParam trae la posicion del cursor en coordenadas de PANTALLA: x en la palabra baja, y en
        // la alta. Se comparan contra GetWindowRect, que devuelve pixeles fisicos igual que lParam,
        // asi no hace falta convertir por escala de pantalla (DPI).
        var x = (short)(lParam.ToInt64() & 0xFFFF);
        var y = (short)((lParam.ToInt64() >> 16) & 0xFFFF);

        if (rect.Right - x > ResizeCornerPx || rect.Bottom - y > ResizeCornerPx)
            return IntPtr.Zero;

        handled = true;
        return new IntPtr(HTBOTTOMRIGHT);
    }

    /// <summary>
    /// Fuerza que la ventana conserve la relacion de aspecto del video mientras se redimensiona, asi
    /// lo enmarca exacto y nunca aparecen franjas negras. Se ajusta el rectangulo que Windows
    /// propone (llega por referencia en lParam) antes de que lo aplique.
    /// </summary>
    private IntPtr HandleSizing(IntPtr lParam, ref bool handled)
    {
        if (DataContext is not PlayerViewModel vm || vm.VideoAspectRatio is not { } aspect || aspect <= 0)
            return IntPtr.Zero;

        var rect = Marshal.PtrToStructure<RECT>(lParam);

        // Lo que no es video (barra de controles y bordes) no entra en la proporcion; se mide en
        // vivo porque depende de la escala de pantalla y del alto real de la barra.
        var dpi = VisualTreeHelper.GetDpi(this);
        var chromeWidthPx = (int)Math.Round((ActualWidth - VideoArea.ActualWidth) * dpi.DpiScaleX);
        var chromeHeightPx = (int)Math.Round((ActualHeight - VideoArea.ActualHeight) * dpi.DpiScaleY);

        if (VideoArea.ActualWidth <= 0 || VideoArea.ActualHeight <= 0)
            return IntPtr.Zero;

        // El ancho manda y el alto se deriva: es lo mas predecible al arrastrar en diagonal.
        var videoWidthPx = rect.Right - rect.Left - chromeWidthPx;
        if (videoWidthPx <= 0)
            return IntPtr.Zero;

        rect.Bottom = rect.Top + (int)Math.Round(videoWidthPx / aspect) + chromeHeightPx;

        Marshal.StructureToPtr(rect, lParam, false);
        handled = true;
        return new IntPtr(1);
    }

    private const int WM_NCHITTEST = 0x0084;
    private const int WM_SIZING = 0x0214;

    /// <summary>Lado (en pixeles de pantalla) del cuadrado agarrable de la esquina. Generoso frente
    /// a la marca visible, que si no seria dificil de acertar.</summary>
    private const int ResizeCornerPx = 18;

    private const int HTBOTTOMRIGHT = 17;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_viewModel is not null)
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;

        _viewModel = e.NewValue as PlayerViewModel;

        if (_viewModel is not null)
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PlayerViewModel.IsReferenceClickThrough))
            ApplyClickThrough();
    }

    /// <summary>
    /// Aplica (o quita) el click-through. Se niega a activarlo si no puede garantizar la salida:
    /// sin el atajo global, los clics atravesarian tambien el boton de apagarlo y la ventana
    /// quedaria inutilizable hasta cerrar la aplicacion.
    /// </summary>
    private void ApplyClickThrough()
    {
        if (_viewModel is null)
            return;

        if (!_viewModel.IsReferenceClickThrough)
        {
            WindowEffectsHelper.SetClickThrough(this, false);
            _exitClickThroughHotkey?.Dispose();
            _exitClickThroughHotkey = null;
            return;
        }

        _exitClickThroughHotkey ??= GlobalHotkey.TryRegister(
            this, ExitClickThroughModifiers, ExitClickThroughKey, ExitClickThrough);

        if (_exitClickThroughHotkey is null)
        {
            _viewModel.IsReferenceClickThrough = false;
            MessageBox.Show(
                this,
                string.Format(Loc.I["Reference_ClickThroughNoHotkey"], ExitClickThroughShortcutText),
                Loc.I["Reference_Title"],
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        // SetClickThrough relee el estilo para confirmar que quedo aplicado de verdad: Windows a
        // veces acepta la llamada sin aplicar el cambio (le paso a WS_EX_LAYERED en esta misma app).
        if (!WindowEffectsHelper.SetClickThrough(this, true))
        {
            _exitClickThroughHotkey.Dispose();
            _exitClickThroughHotkey = null;
            _viewModel.IsReferenceClickThrough = false;
            MessageBox.Show(
                this,
                Loc.I["Reference_ClickThroughFailed"],
                Loc.I["Reference_Title"],
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        MessageBox.Show(
            this,
            string.Format(Loc.I["Reference_ClickThroughOn"], ExitClickThroughShortcutText),
            Loc.I["Reference_Title"],
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void ExitClickThrough()
    {
        if (_viewModel is null)
            return;

        _viewModel.IsReferenceClickThrough = false;

        // Devolverle el foco: se acaba de recuperar el control de la ventana, lo esperable es poder
        // usarla enseguida.
        Activate();
    }

    // Sin barra de titulo nativa (la exige AllowsTransparency), asi que el arrastre se hace a mano
    // desde la barra propia.
    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed)
            return;

        // DragMove tira excepcion si el boton ya se solto entre el evento y la llamada.
        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void OnClosed(object? sender, EventArgs e)
    {
        _exitClickThroughHotkey?.Dispose();
        _exitClickThroughHotkey = null;

        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            // El click-through es propio de esta ventana: no debe quedar marcado si se reabre.
            _viewModel.IsReferenceClickThrough = false;
            _viewModel = null;
        }
    }
}
