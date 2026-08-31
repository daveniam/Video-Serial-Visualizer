// Video Serial Visualizer - Copyright (C) 2026  David Nieves
// SPDX-License-Identifier: GPL-3.0-or-later
// Software libre, sin garantia alguna. Ver LICENSE para los terminos completos.

using System.IO;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using LibVLCSharp.Shared;
using Microsoft.Data.Sqlite;
using VideoSerialVisualizer.Data;
using VideoSerialVisualizer.Localization;
using VideoSerialVisualizer.Models;
using VideoSerialVisualizer.Services;

namespace VideoSerialVisualizer.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable
{
    private LibVLC? _libVlc;
    private Task<LibVLC>? _libVlcTask;
    private Task? _playerReadyTask;

    [ObservableProperty]
    private object? currentViewModel;

    /// <summary>El sello de version (esquina inferior derecha) se oculta en el reproductor: alli esa
    /// zona la ocupa la barra de controles y el sello quedaria encima.</summary>
    public bool IsVersionBadgeVisible => CurrentViewModel is not global::VideoSerialVisualizer.ViewModels.PlayerViewModel;

    partial void OnCurrentViewModelChanged(object? value) => OnPropertyChanged(nameof(IsVersionBadgeVisible));

    [ObservableProperty]
    private bool isLoading = true;

    [ObservableProperty]
    private string loadingMessage = Loc.I["Startup_Starting"];

    public FoldersViewModel? FoldersViewModel { get; private set; }
    public LibraryViewModel? LibraryViewModel { get; private set; }
    public PlayerViewModel? PlayerViewModel { get; private set; }

    public async Task InitializeAsync()
    {
        // LibVLC se inicializa en SEGUNDO PLANO y NO se espera aca: la pantalla de Explorar no lo
        // necesita (solo lee la base y las miniaturas de disco). Sacarlo del camino critico hace que
        // la app muestre contenido bastante antes (medido: ~200 ms en tibio, ~560 ms en frio, que es
        // lo que tardaba en escanear los 322 plugins de VLC). Se espera recien cuando hace falta:
        // escanear una carpeta (FolderScannerService), generar miniaturas/portadas (ThumbnailService)
        // o reproducir (PlayerViewModel, ver InitializePlayerAsync).
        _libVlcTask = Task.Run(() =>
        {
            Core.Initialize();
            // Se desactiva la decodificacion por hardware: es la causa mas comun de crashes
            // nativos de LibVLC con ciertos drivers de GPU/codecs (no recuperable con try/catch).
            return new LibVLC("--avcodec-hw=none");
        });

        LoadingMessage = Loc.I["Startup_Database"];
        await using (var db = new AppDbContext())
        {
            try
            {
                await AppDbContext.EnsureSchemaUpToDateAsync(db);
            }
            catch (SqliteException)
            {
                // Otra instancia creo el esquema al mismo tiempo; el resultado final es el mismo.
            }
        }

        // Los servicios que dependen de LibVLC lo resuelven de forma perezosa (esperan _libVlcTask
        // recien al usarlo), asi se construyen sin bloquear el arranque.
        var thumbnailService = new ThumbnailService(GetLibVlcAsync);
        var scannerService = new FolderScannerService(GetLibVlcAsync, thumbnailService);
        var progressTracker = new ProgressTrackerService();
        var markerService = new VideoMarkerService();

        FoldersViewModel = new FoldersViewModel(scannerService, OpenFolder);
        LibraryViewModel = new LibraryViewModel(OpenPlayer, BackToFolders);

        OnPropertyChanged(nameof(FoldersViewModel));
        OnPropertyChanged(nameof(LibraryViewModel));

        LoadingMessage = Loc.I["Startup_Library"];
        await FoldersViewModel.InitializeAsync(); // no usa LibVLC

        CurrentViewModel = FoldersViewModel;
        IsLoading = false; // Explorar ya visible, sin haber esperado a LibVLC

        // El reproductor SI necesita LibVLC en su constructor (crea un MediaPlayer), asi que se arma
        // en cuanto LibVLC termina (en segundo plano). Para cuando el usuario navegue hasta un video
        // ya suele estar listo; si no, OpenPlayer espera esta tarea.
        _playerReadyTask = InitializePlayerAsync(progressTracker, markerService, thumbnailService);
    }

    private Task<LibVLC> GetLibVlcAsync() => _libVlcTask!;

    private async Task InitializePlayerAsync(ProgressTrackerService progressTracker, VideoMarkerService markerService, ThumbnailService thumbnailService)
    {
        _libVlc = await _libVlcTask!;
        PlayerViewModel = new PlayerViewModel(_libVlc, progressTracker, markerService, thumbnailService, BackToLibrary);
        OnPropertyChanged(nameof(PlayerViewModel));
    }

    private async void OpenFolder(string folderPath)
    {
        var folderName = Path.GetFileName(folderPath.TrimEnd('\\', '/'));
        if (string.IsNullOrEmpty(folderName))
            folderName = folderPath;

        LibraryViewModel!.SetScope(folderPath, folderName);
        CurrentViewModel = LibraryViewModel;
        await LibraryViewModel.RefreshAsync();
    }

    private async void BackToFolders()
    {
        CurrentViewModel = FoldersViewModel;
        await FoldersViewModel!.RefreshAsync();
    }

    private async void OpenPlayer(Video video)
    {
        // El reproductor puede no estar armado todavia si LibVLC aun se esta inicializando (arranque
        // en frio). Se espera aca; en la practica, para cuando el usuario llego hasta un video ya
        // esta listo.
        if (_playerReadyTask is not null)
            await _playerReadyTask;

        if (PlayerViewModel is null)
            return;

        // Mostrar primero el reproductor para que PlayerView se cargue y registre su ventana de
        // video (Hwnd). Se espera a la prioridad Loaded del Dispatcher para garantizar que ese
        // registro ya ocurrio ANTES de reproducir; de lo contrario LibVLC abre su propia ventana.
        CurrentViewModel = PlayerViewModel;
        await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Loaded);
        await PlayerViewModel!.LoadVideoAsync(video, LibraryViewModel!.GetPlaylist());
    }

    private async void BackToLibrary()
    {
        CurrentViewModel = LibraryViewModel;
        await LibraryViewModel!.RefreshAsync();
    }

    public void Dispose()
    {
        PlayerViewModel?.SaveAndDispose();

        // Si se cierra durante el arranque (antes de que el reproductor se arme), _libVlc puede ser
        // null pero la tarea de fondo ya haber creado la instancia: se libera igual.
        var vlc = _libVlc ?? (_libVlcTask is { IsCompletedSuccessfully: true } t ? t.Result : null);
        vlc?.Dispose();
    }
}
