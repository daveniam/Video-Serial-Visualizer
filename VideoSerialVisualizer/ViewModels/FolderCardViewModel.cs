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

    /// <summary>Linea bajo el nombre del grupo: cantidad de videos y, si se conoce, la duracion total
    /// ("12 video(s)  ·  8h 30min"). La duracion se omite si es 0 (todavia sin escanear o desconocida).</summary>
    public string InfoText
    {
        get
        {
            var videos = $"{VideoCount}{Loc.I["Common_VideosSuffix"]}";
            var duration = FormatDuration(TotalDurationMs);
            return string.IsNullOrEmpty(duration) ? videos : $"{videos}  ·  {duration}";
        }
    }

    /// <summary>Formatea una duracion total en "Xh Ymin" (o solo minutos si es menos de una hora).
    /// Devuelve vacio si es 0.</summary>
    private static string FormatDuration(long totalMs)
    {
        if (totalMs <= 0)
            return string.Empty;

        var span = TimeSpan.FromMilliseconds(totalMs);
        var hours = (int)span.TotalHours;
        var minutes = span.Minutes;

        var h = Loc.I["Common_HoursShort"];
        var m = Loc.I["Common_MinutesShort"];

        if (hours > 0)
            return minutes > 0 ? $"{hours}{h} {minutes}{m}" : $"{hours}{h}";

        // Menos de una hora: al menos "1min" si hay algo de duracion, para no mostrar "0min".
        return $"{Math.Max(1, minutes)}{m}";
    }

    [ObservableProperty]
    private string displayName;

    [ObservableProperty]
    private bool isActive;

    [ObservableProperty]
    private bool favorito;

    [ObservableProperty]
    private int? categoryId;

    [ObservableProperty]
    private ImageSource? thumbnailImage;

    public FolderCardViewModel(string folderPath, int videoCount, long totalDurationMs, string? thumbnailPath, string? customDisplayName, bool favorito, int? categoryId)
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
        this.categoryId = categoryId;
    }

    public async Task LoadThumbnailAsync()
    {
        if (string.IsNullOrEmpty(ThumbnailPath))
            return;

        ThumbnailImage = await ThumbnailLoader.LoadCachedAsync(ThumbnailPath);
    }
}
