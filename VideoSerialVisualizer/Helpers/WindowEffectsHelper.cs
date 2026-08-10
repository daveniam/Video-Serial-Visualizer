// Video Serial Visualizer - Copyright (C) 2026  David Nieves
// SPDX-License-Identifier: GPL-3.0-or-later
// Software libre, sin garantia alguna. Ver LICENSE para los terminos completos.

using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace VideoSerialVisualizer.Helpers;

/// <summary>
/// Efectos Win32 de la ventana de referencia flotante.
///
/// NOTA sobre la opacidad: NO se hace desde aca. Se probo aplicar WS_EX_LAYERED +
/// SetLayeredWindowAttributes sobre una ventana WPF ya creada y **no funciona**: SetWindowLong
/// devuelve exito pero el bit nunca queda puesto (WPF tiene su render target de DirectX atado al
/// HWND). La unica via que funciona es AllowsTransparency=true de WPF, fijado antes de mostrar la
/// ventana, y por eso la ventana de referencia nace asi y usa la propiedad Opacity normal.
/// </summary>
public static class WindowEffectsHelper
{
    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_TRANSPARENT = 0x00000020;
    private const long WS_EX_NOACTIVATE = 0x08000000;

    /// <summary>
    /// Marca una ventana como "no activable" (WS_EX_NOACTIVATE). Se usa sobre la ventana que Win32
    /// crea por debajo de un Popup de WPF con AllowsTransparency: sin esto, ese Popup transparente
    /// roba la activacion y provoca dos sintomas al minimizar/restaurar la ventana principal: un
    /// pitido de error y que haga falta un segundo clic en la barra de tareas para restaurar. La
    /// ventana sigue recibiendo clics (el boton adentro funciona igual), solo deja de tomar el foco.
    /// </summary>
    public static void SetNoActivate(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return;

        try
        {
            var exStyle = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
            SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(exStyle | WS_EX_NOACTIVATE));

            // Cambiar el estilo extendido no siempre surte efecto hasta un "frame change": se fuerza
            // con SetWindowPos (sin mover, sin redimensionar, sin cambiar z-order ni activar).
            SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
        }
        catch
        {
            // Best effort: si falla, el Popup sigue funcionando, solo con los sintomas de activacion.
        }
    }

    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_FRAMECHANGED = 0x0020;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    /// <summary>
    /// Activa o desactiva el "click-through": con el activado, los clics atraviesan la ventana y
    /// llegan a la aplicacion de abajo, asi se puede dibujar sobre la referencia sin que esta robe
    /// el clic ni el foco.
    ///
    /// Devuelve true si el estado quedo aplicado de verdad (se relee el estilo para confirmarlo, en
    /// vez de confiar en el valor de retorno de la API).
    /// </summary>
    public static bool SetClickThrough(Window? window, bool enabled)
    {
        if (window is null)
            return false;

        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
            return false;

        try
        {
            var exStyle = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
            var target = enabled ? exStyle | WS_EX_TRANSPARENT : exStyle & ~WS_EX_TRANSPARENT;

            if (exStyle != target)
                SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(target));

            var applied = (GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64() & WS_EX_TRANSPARENT) != 0;
            return applied == enabled;
        }
        catch
        {
            return false;
        }
    }

    // Variantes ...Ptr: la app es x64-only (ver el .csproj); las de 32 bits truncarian el estilo.
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
}
