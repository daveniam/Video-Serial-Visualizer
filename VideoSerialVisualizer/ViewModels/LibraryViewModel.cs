// Video Serial Visualizer - Copyright (C) 2026  David Nieves
// SPDX-License-Identifier: GPL-3.0-or-later
// Software libre, sin garantia alguna. Ver LICENSE para los terminos completos.

using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using VideoSerialVisualizer.Data;
using VideoSerialVisualizer.Helpers;
using VideoSerialVisualizer.Models;

namespace VideoSerialVisualizer.ViewModels;

public partial class LibraryViewModel : ObservableObject
{
    private readonly Action<Video> _openVideo;
    private readonly Action _goBack;
    private List<VideoCardViewModel> _allVideos = new();

    private string? _folderFilter;

    public ObservableCollection<VideoCardViewModel> Videos { get; } = new();

    [ObservableProperty]
    private string scopeTitle = string.Empty;

    [ObservableProperty]
    private string searchText = string.Empty;

    [ObservableProperty]
    private bool isListView;

    /// <summary>Muestra la ruedita de carga mientras se arma el grupo.</summary>
    [ObservableProperty]
    private bool isLoading;

    public LibraryViewModel(Action<Video> openVideo, Action goBack)
    {
        _openVideo = openVideo;
        _goBack = goBack;
    }

    public void SetScope(string? folderFilter, string title)
    {
        _folderFilter = folderFilter;
        ScopeTitle = title;
        SearchText = string.Empty;
    }

    [RelayCommand]
    private void OpenVideo(VideoCardViewModel? card)
    {
        if (card is null)
            return;

        _openVideo(card.Video);
    }

    /// <summary>
    /// Lista de reproduccion en el orden que ve el usuario (respeta el filtro de busqueda actual).
    /// La usa el reproductor para saber cual es el "siguiente" video.
    /// </summary>
    public IReadOnlyList<Video> GetPlaylist() => Videos.Select(v => v.Video).ToList();

    [RelayCommand]
    private void Back() => _goBack();

    [RelayCommand]
    private void SetGridView() => IsListView = false;

    [RelayCommand]
    private void SetListView() => IsListView = true;

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    /// <summary>
    /// Entra al grupo mostrando la ruedita y carga con ella girando. El truco esta en el orden:
    /// primero se prende la ruedita y se CEDE un cuadro (prioridad Background corre despues del
    /// render) para que la vista + la ruedita se dibujen ANTES del trabajo pesado. Armar las tarjetas
    /// bloquea el hilo de UI, pero la animacion de la ruedita corre en el hilo de composicion, asi
    /// sigue girando durante ese bloqueo en vez de dejar la vista congelada. Se apaga al terminar
    /// (tras otro ciclo Background, que corre despues del layout de las tarjetas).
    /// </summary>
    public async Task RefreshWithSpinnerAsync()
    {
        IsLoading = true;
        await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);

        try
        {
            await RefreshAsync();
            await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task RefreshAsync()
    {
        await using var db = new AppDbContext();

        // Se filtra por carpeta en SQL (hay indice en CarpetaOrigen) en vez de traer toda la tabla
        // y descartar en memoria; el progreso se acota a los videos que realmente se van a mostrar.
        var query = db.Videos.AsNoTracking();
        if (!string.IsNullOrEmpty(_folderFilter))
            query = query.Where(v => v.CarpetaOrigen == _folderFilter);

        var videos = await query.ToListAsync();

        var videoIds = videos.Select(v => v.Id).ToList();
        var progressByVideoId = await db.Progress.AsNoTracking()
            .Where(p => videoIds.Contains(p.VideoId))
            .ToDictionaryAsync(p => p.VideoId);

        _allVideos = videos
            .OrderBy(v => v.NombreArchivo, NaturalStringComparer.Instance)
            .Select(v => new VideoCardViewModel(v, progressByVideoId.GetValueOrDefault(v.Id)))
            .ToList();

        ApplyFilter();

        foreach (var video in _allVideos)
            _ = video.LoadThumbnailAsync();
    }

    private void ApplyFilter()
    {
        Videos.Clear();

        var filtered = string.IsNullOrWhiteSpace(SearchText)
            ? _allVideos
            : _allVideos.Where(v => v.NombreArchivo.Contains(SearchText, StringComparison.OrdinalIgnoreCase));

        foreach (var video in filtered)
            Videos.Add(video);
    }
}
