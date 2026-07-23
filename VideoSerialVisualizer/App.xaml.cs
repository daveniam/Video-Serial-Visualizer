// Video Serial Visualizer - Copyright (C) 2026  David Nieves
// SPDX-License-Identifier: GPL-3.0-or-later
// Software libre, sin garantia alguna. Ver LICENSE para los terminos completos.

using System.Threading;
using System.Windows;
using VideoSerialVisualizer.Localization;
using VideoSerialVisualizer.Services;
using VideoSerialVisualizer.ViewModels;

namespace VideoSerialVisualizer;

public partial class App : Application
{
    private readonly UpdateService _updateService = new();
    private Mutex? _singleInstanceMutex;
    private MainViewModel? _mainViewModel;

    protected override async void OnStartup(StartupEventArgs e)
    {
        // Nota: VelopackApp.Build().Run() se llama en Program.Main, antes de inicializar WPF.
        base.OnStartup(e);

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;

        _singleInstanceMutex = new Mutex(true, "VideoSerialVisualizer-SingleInstance-9F3B2A7C", out var createdNew);
        if (!createdNew)
        {
            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
            MessageBox.Show(Loc.I["App_AlreadyRunning"], "Video Serial Visualizer", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        _mainViewModel = new MainViewModel();

        var mainWindow = new MainWindow(_mainViewModel);
        MainWindow = mainWindow;
        mainWindow.Show();

        await _mainViewModel.InitializeAsync();

        // Recien despues de que la app esta usable: buscar y bajar la actualizacion en segundo
        // plano, sin bloquear ni molestar. Se instala al cerrar (ver OnExit).
        await _updateService.CheckAndDownloadAsync();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _mainViewModel?.Dispose();
        _updateService.ApplyPendingUpdateOnExit();

        if (_singleInstanceMutex is not null)
        {
            _singleInstanceMutex.ReleaseMutex();
            _singleInstanceMutex.Dispose();
        }

        base.OnExit(e);
    }

    private static void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(
            string.Format(Loc.I["Error_Unexpected_Handled"], e.Exception.Message),
            Loc.I["Error_Unexpected_Title"],
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
        e.Handled = true;
    }

    private static void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            MessageBox.Show(
                string.Format(Loc.I["Error_Unexpected_Message"], ex.Message),
                Loc.I["Error_Unexpected_Title"],
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }
}
