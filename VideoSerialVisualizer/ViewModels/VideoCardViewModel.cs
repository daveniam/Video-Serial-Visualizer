// Video Serial Visualizer - Copyright (C) 2026  David Nieves
// SPDX-License-Identifier: GPL-3.0-or-later
// Software libre, sin garantia alguna. Ver LICENSE para los terminos completos.

using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using VideoSerialVisualizer.Helpers;
using VideoSerialVisualizer.Models;

namespace VideoSerialVisualizer.ViewModels;

public partial class VideoCardViewModel : ObservableObject
{
    public Video Video { get; }

    public int Id => Video.Id;
    public string NombreArchivo => Video.NombreArchivo;
    public string CarpetaOrigen => Video.CarpetaOrigen;
    public string? ThumbnailPath => Video.ThumbnailPath;
    public bool Completado { get; }
    public double ProgressPercent { get; }
    public string DurationText { get; }
    public string ProgressText { get; }

    [ObservableProperty]
    private ImageSource? thumbnailImage;

    public VideoCardViewModel(Video video, WatchProgress? progress)
    {
        Video = video;
        Completado = progress?.Completado ?? false;
        DurationText = TimeFormatter.Format(video.DuracionMs);

        if (progress is null || video.DuracionMs <= 0)
        {
            ProgressPercent = 0;
        }
        else
        {
            ProgressPercent = Math.Clamp(progress.PosicionMs / (double)video.DuracionMs * 100.0, 0, 100);
        }

        ProgressText = Completado ? "Completado" : ProgressPercent > 0 ? $"{ProgressPercent:0}% visto" : "Sin ver";
    }

    public async Task LoadThumbnailAsync()
    {
        if (string.IsNullOrEmpty(ThumbnailPath))
            return;

        ThumbnailImage = await ThumbnailLoader.LoadCachedAsync(ThumbnailPath);
    }
}
