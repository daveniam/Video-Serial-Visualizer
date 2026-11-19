// Video Serial Visualizer - Copyright (C) 2026  David Nieves
// SPDX-License-Identifier: GPL-3.0-or-later
// Software libre, sin garantia alguna. Ver LICENSE para los terminos completos.

using System;
using System.IO;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using VideoSerialVisualizer.Helpers;
using VideoSerialVisualizer.Localization;

namespace VideoSerialVisualizer.ViewModels;

public partial class FolderCardViewModel : ObservableObject
{
    public string FolderPath { get; }
    public string FolderName { get; }
    public int VideoCount { get; }
    public long TotalDurationMs { get; }
    public string? ThumbnailPath { get; }
    public int WatchedCount { get; }

    /// <summary>Porcentaje de videos vistos del grupo (0-100), para la linea de progreso de la tarjeta.</summary>
    public double WatchedPercent => VideoCount > 0 ? WatchedCount / (double)VideoCount * 100.0 : 0;

    /// <summary>Todos los videos del grupo estan vistos: la linea de progreso se pinta en verde, igual
    /// que "Completado" en la tarjeta de un video individual.</summary>
    public bool AllWatched => VideoCount > 0 && WatchedCount == VideoCount;

    /// <summary>Texto del tooltip de la linea de progreso ("3/12 vistos").</summary>
    public string WatchedTooltip => string.Format(Loc.I["Explore_WatchedProgress"], WatchedCount, VideoCount);

    /// <summary>Linea bajo el nombre del grupo: cantidad de videos y, si se conoce, la duracion total
    /// ("12 video(s)  ·  8h 30min"). La duracion se omite si es 0 (todavia sin escanear o desconocida).</summary>
    public string InfoText
    {
        get
        {
            var videos = $"{VideoCount}{Loc.I["Common_VideosSuffix"]}";
            var duration = DurationFormat.Total(TotalDurationMs);
            return string.IsNullOrEmpty(duration) ? videos : $"{videos}  ·  {duration}";
        }
    }

    [ObservableProperty]
    private string displayName;

    [ObservableProperty]
    private bool isActive;

    [ObservableProperty]
    private bool favorito;

    /// <summary>Categorias asignadas al grupo (puede ser mas de una). Se usa para el filtro por
    /// categoria en Explorar. Se reemplaza entero al reasignar; no hace falta que sea observable
    /// porque no se muestra en la tarjeta.</summary>
    public IReadOnlyCollection<int> CategoryIds { get; set; }

    [ObservableProperty]
    private ImageSource? thumbnailImage;

    /// <summary>Curso al que pertenece (null = suelto en Explorar).</summary>
    public int? CourseId { get; set; }

    /// <summary>Posicion dentro de su curso.</summary>
    public int CourseOrder { get; set; }

    /// <summary>Numero de orden visible dentro del curso (1, 2, 3...).</summary>
    [ObservableProperty]
    private int position;

    /// <summary>Se esta arrastrando otro grupo encima (reordenar dentro del curso): resalta el borde.</summary>
    [ObservableProperty]
    private bool isDropTarget;

    public FolderCardViewModel(string folderPath, int videoCount, long totalDurationMs, string? thumbnailPath, string? customDisplayName, bool favorito, IReadOnlyCollection<int> categoryIds, int watchedCount)
    {
        FolderPath = folderPath;

        FolderName = Path.GetFileName(folderPath.TrimEnd('\\', '/'));
        if (string.IsNullOrEmpty(FolderName))
            FolderName = folderPath;

        VideoCount = videoCount;
        TotalDurationMs = totalDurationMs;
        ThumbnailPath = thumbnailPath;
        displayName = string.IsNullOrWhiteSpace(customDisplayName) ? FolderName : customDisplayName;
        this.favorito = favorito;
        CategoryIds = categoryIds;
        WatchedCount = watchedCount;
    }

    public async Task LoadThumbnailAsync()
    {
        if (string.IsNullOrEmpty(ThumbnailPath))
            return;

        ThumbnailImage = await ThumbnailLoader.LoadCachedAsync(ThumbnailPath);
    }
}
