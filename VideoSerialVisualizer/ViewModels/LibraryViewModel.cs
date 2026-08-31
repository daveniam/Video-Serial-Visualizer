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

    /// <summary>Muestra la ruedita de carga. Solo se prende si abrir el grupo tarda mas de 1s (ver
    /// RefreshWithSpinnerAsync), asi los grupos rapidos no parpadean.</summary>
    [ObservableProperty]
    private bool isLoading;

    /// <summary>Umbral antes de mostrar la ruedita al abrir un grupo.</summary>
    private const int SpinnerDelayMs = 1000;

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
    /// Carga el grupo mostrando la ruedita SOLO si tarda mas de 1s. Un contador arranca en paralelo:
    /// si la carga termina antes, se cancela y la ruedita nunca aparece (grupos rapidos, sin
    /// parpadeo); si tarda mas, se prende y se apaga al terminar. Se espera ademas a una devolucion
    /// de control en prioridad Background (corre despues del layout), asi la ruedita cubre tambien el
    /// armado de las tarjetas y no solo la consulta a la base.
    /// </summary>
    public async Task RefreshWithSpinnerAsync()
    {
        using var loaded = new CancellationTokenSource();
        var spinnerDelay = ShowSpinnerAfterDelayAsync(loaded.Token);

        try
        {
            await RefreshAsync();
            await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
        }
        finally
        {
            loaded.Cancel();
            IsLoading = false;
        }

        await spinnerDelay; // observa la tarea del contador (ya cancelada) para no dejarla suelta
    }

    private async Task ShowSpinnerAfterDelayAsync(CancellationToken cancelWhenLoaded)
    {
        try
        {
            await Task.Delay(SpinnerDelayMs, cancelWhenLoaded);
            IsLoading = true; // seguimos cargando al pasar el umbral
        }
        catch (TaskCanceledException)
        {
            // La carga termino antes del umbral: la ruedita no llega a mostrarse.
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
