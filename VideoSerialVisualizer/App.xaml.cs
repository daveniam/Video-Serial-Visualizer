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

    /// <summary>
    /// Evita que un error que se repite en cada repintado tape la pantalla de dialogos.
    ///
    /// MessageBox.Show bombea la cola de mensajes de Windows: mientras el dialogo esta abierto, WPF
    /// sigue procesando layout/render, y si la excepcion venia justo de ahi (p.ej. un binding
    /// invalido en un template) vuelve a dispararse, entra de nuevo a este handler y abre OTRO
    /// dialogo sobre el anterior. La recursion no para hasta agotar la pila del proceso
    /// ("a new guard page for the stack cannot be created"). Con esta guarda solo se muestra el
    /// primer error; los que lleguen mientras ese dialogo sigue abierto se marcan como manejados
    /// en silencio.
    /// </summary>
    private static bool _isShowingUnhandledErrorDialog;

    private static void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        // Siempre se marca manejado: la app sigue viva aunque no se muestre este error puntual.
        e.Handled = true;

        if (_isShowingUnhandledErrorDialog)
            return;

        _isShowingUnhandledErrorDialog = true;
        try
        {
            MessageBox.Show(
                string.Format(Loc.I["Error_Unexpected_Handled"], e.Exception.Message),
                Loc.I["Error_Unexpected_Title"],
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        finally
        {
            _isShowingUnhandledErrorDialog = false;
        }
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
