// Video Serial Visualizer - Copyright (C) 2026  David Nieves
// SPDX-License-Identifier: GPL-3.0-or-later
// Software libre, sin garantia alguna. Ver LICENSE para los terminos completos.

using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using VideoSerialVisualizer.Helpers;
using VideoSerialVisualizer.Localization;
using VideoSerialVisualizer.Models;

namespace VideoSerialVisualizer.ViewModels;

public partial class VideoCardViewModel : ObservableObject
{
    public Video Video { get; }

    public int Id => Video.Id;
    public string NombreArchivo => Video.NombreArchivo;
    public string CarpetaOrigen => Video.CarpetaOrigen;
    public string? ThumbnailPath => Video.ThumbnailPath;
    public string DurationText { get; }

    [ObservableProperty]
    private bool completado;

    [ObservableProperty]
    private double progressPercent;

    [ObservableProperty]
    private string progressText = string.Empty;

    /// <summary>Texto del item de menu "Marcar como visto"/"Marcar como no visto" (cambia con Completado).</summary>
    [ObservableProperty]
    private string toggleWatchedMenuText = string.Empty;

    [ObservableProperty]
    private ImageSource? thumbnailImage;

    public VideoCardViewModel(Video video, WatchProgress? progress)
    {
        Video = video;
        DurationText = TimeFormatter.Format(video.DuracionMs);
        ApplyProgress(progress);
    }

    /// <summary>
    /// Recalcula el estado de progreso mostrado en la tarjeta. Se llama desde el constructor y de
    /// nuevo despues de marcar/desmarcar como visto, para reflejar el cambio sin rearmar la tarjeta.
    /// </summary>
    public void ApplyProgress(WatchProgress? progress)
    {
        Completado = progress?.Completado ?? false;

        ProgressPercent = progress is null || Video.DuracionMs <= 0
            ? 0
            : Math.Clamp(progress.PosicionMs / (double)Video.DuracionMs * 100.0, 0, 100);

        ProgressText = Completado
            ? Loc.I["Progress_Completed"]
            : ProgressPercent > 0
                ? string.Format(Loc.I["Progress_Watched"], Math.Round(ProgressPercent))
                : Loc.I["Progress_Unwatched"];

        ToggleWatchedMenuText = Completado
            ? Loc.I["Library_MarkUnwatched"]
            : Loc.I["Library_MarkWatched"];
    }

    public async Task LoadThumbnailAsync()
    {
        if (string.IsNullOrEmpty(ThumbnailPath))
            return;

        ThumbnailImage = await ThumbnailLoader.LoadCachedAsync(ThumbnailPath);
    }
}
