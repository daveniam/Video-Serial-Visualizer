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
        LoadingMessage = Loc.I["Startup_Player"];
        await Task.Run(() =>
        {
            Core.Initialize();
            // Se desactiva la decodificacion por hardware: es la causa mas comun de crashes
            // nativos de LibVLC con ciertos drivers de GPU/codecs (no recuperable con try/catch).
            _libVlc = new LibVLC("--avcodec-hw=none");
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

        var thumbnailService = new ThumbnailService(_libVlc!);
        var scannerService = new FolderScannerService(_libVlc!, thumbnailService);
        var progressTracker = new ProgressTrackerService();
        var markerService = new VideoMarkerService();

        FoldersViewModel = new FoldersViewModel(scannerService, OpenFolder);
        LibraryViewModel = new LibraryViewModel(OpenPlayer, BackToFolders);
        PlayerViewModel = new PlayerViewModel(_libVlc!, progressTracker, markerService, thumbnailService, BackToLibrary);

        OnPropertyChanged(nameof(FoldersViewModel));
        OnPropertyChanged(nameof(LibraryViewModel));
        OnPropertyChanged(nameof(PlayerViewModel));

        LoadingMessage = Loc.I["Startup_Library"];
        await FoldersViewModel.InitializeAsync();

        CurrentViewModel = FoldersViewModel;
        IsLoading = false;
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
        _libVlc?.Dispose();
    }
}
