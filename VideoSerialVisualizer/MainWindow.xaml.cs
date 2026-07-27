// Video Serial Visualizer - Copyright (C) 2026  David Nieves
// SPDX-License-Identifier: GPL-3.0-or-later
// Software libre, sin garantia alguna. Ver LICENSE para los terminos completos.

using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using VideoSerialVisualizer.ViewModels;

namespace VideoSerialVisualizer;

public partial class MainWindow : Window
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    // Indice para reemplazar el pincel de fondo de la clase de ventana (SetClassLongPtr).
    private const int GCLP_HBRBACKGROUND = -10;
    private const int BLACK_BRUSH = 4;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        // Sello de version discreto (esquina inferior derecha). Se toma del ensamblado para no
        // duplicar el numero que ya vive en el .csproj.
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        VersionBadge.Text = "v" + (version?.ToString(3) ?? "1.0.0");
        SourceInitialized += (_, _) =>
        {
            EnableDarkTitleBar();
            UseBlackWindowBackgroundBrush();
            SuppressBackgroundErase();
        };
    }

    /// <summary>
    /// Al maximizar/redimensionar, Windows manda WM_ERASEBKGND para borrar el area nueva antes de
    /// que WPF y el video repinten: eso es lo que se ve como un destello blanco. Se responde que ya
    /// esta atendido (1) para que no borre nada; WPF igual repinta todo el area.
    /// </summary>
    private void SuppressBackgroundErase()
    {
        var source = PresentationSource.FromVisual(this) as HwndSource;
        source?.AddHook(WndProcHook);
    }

    private static IntPtr WndProcHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_ERASEBKGND = 0x0014;

        if (msg == WM_ERASEBKGND)
        {
            handled = true;
            return new IntPtr(1);
        }

        return IntPtr.Zero;
    }

    private void EnableDarkTitleBar()
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var useDarkMode = 1;
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDarkMode, sizeof(int));
        }
        catch
        {
            // Best effort: versiones viejas de Windows no soportan este atributo.
        }
    }

    /// <summary>
    /// Al maximizar/agrandar, Windows borra el area nueva con el pincel de fondo de la clase de
    /// ventana Win32, que por defecto es BLANCO. Eso causa un destello blanco antes de que WPF
    /// (y el video nativo) repinten. Se reemplaza por un pincel negro de stock (compartido, no hay
    /// que liberarlo), asi el area expuesta durante el resize queda negra en vez de blanca.
    /// </summary>
    private void UseBlackWindowBackgroundBrush()
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var blackBrush = GetStockObject(BLACK_BRUSH);
            SetClassLongPtr(hwnd, GCLP_HBRBACKGROUND, blackBrush);
        }
        catch
        {
            // Best effort.
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int valueSize);

    [DllImport("gdi32.dll")]
    private static extern IntPtr GetStockObject(int fnObject);

    // SetClassLongPtr existe solo en la version de 64 bits de user32; la app es x64-only.
    [DllImport("user32.dll", EntryPoint = "SetClassLongPtrW")]
    private static extern IntPtr SetClassLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
}
