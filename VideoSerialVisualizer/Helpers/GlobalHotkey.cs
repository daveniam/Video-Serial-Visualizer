// Video Serial Visualizer - Copyright (C) 2026  David Nieves
// SPDX-License-Identifier: GPL-3.0-or-later
// Software libre, sin garantia alguna. Ver LICENSE para los terminos completos.

using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace VideoSerialVisualizer.Helpers;

/// <summary>
/// Atajo de teclado a nivel de todo el sistema: funciona aunque la aplicacion no tenga el foco.
///
/// Existe por el modo click-through de la ventana de referencia: con los clics atravesando la
/// ventana, tampoco se puede clickear el boton para apagarlo, asi que la unica salida posible es
/// una combinacion de teclas global.
///
/// El registro PUEDE FALLAR si otra aplicacion ya se quedo con esa combinacion; en ese caso
/// <see cref="TryRegister"/> devuelve null y el llamador debe abstenerse de activar el modo, para
/// no dejar al usuario sin forma de salir.
/// </summary>
public sealed class GlobalHotkey : IDisposable
{
    private const int WM_HOTKEY = 0x0312;

    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const uint MOD_WIN = 0x0008;

    /// <summary>Evita que mantener las teclas apretadas dispare el atajo una y otra vez.</summary>
    private const uint MOD_NOREPEAT = 0x4000;

    private readonly IntPtr _hwnd;
    private readonly int _id;
    private readonly HwndSource _source;
    private readonly Action _onPressed;
    private bool _isDisposed;

    private GlobalHotkey(IntPtr hwnd, int id, HwndSource source, Action onPressed)
    {
        _hwnd = hwnd;
        _id = id;
        _source = source;
        _onPressed = onPressed;
        _source.AddHook(WndProcHook);
    }

    /// <summary>
    /// Intenta registrar el atajo. Devuelve null si no se pudo (tipicamente porque otra aplicacion
    /// ya lo tiene tomado, o la ventana todavia no tiene handle).
    /// </summary>
    public static GlobalHotkey? TryRegister(Window window, ModifierKeys modifiers, Key key, Action onPressed)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
            return null;

        if (HwndSource.FromHwnd(hwnd) is not { } source)
            return null;

        uint nativeModifiers = MOD_NOREPEAT;
        if (modifiers.HasFlag(ModifierKeys.Alt)) nativeModifiers |= MOD_ALT;
        if (modifiers.HasFlag(ModifierKeys.Control)) nativeModifiers |= MOD_CONTROL;
        if (modifiers.HasFlag(ModifierKeys.Shift)) nativeModifiers |= MOD_SHIFT;
        if (modifiers.HasFlag(ModifierKeys.Windows)) nativeModifiers |= MOD_WIN;

        var virtualKey = (uint)KeyInterop.VirtualKeyFromKey(key);

        // El id solo tiene que ser unico dentro de este proceso.
        var id = virtualKey.GetHashCode() ^ nativeModifiers.GetHashCode();

        try
        {
            if (!RegisterHotKey(hwnd, id, nativeModifiers, virtualKey))
                return null;
        }
        catch
        {
            return null;
        }

        return new GlobalHotkey(hwnd, id, source, onPressed);
    }

    private IntPtr WndProcHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == _id)
        {
            handled = true;
            _onPressed();
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;

        try
        {
            _source.RemoveHook(WndProcHook);
            UnregisterHotKey(_hwnd, _id);
        }
        catch
        {
            // best effort: si la ventana ya murio, Windows libera el atajo solo.
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
