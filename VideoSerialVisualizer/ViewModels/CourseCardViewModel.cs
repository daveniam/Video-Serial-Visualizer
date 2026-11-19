// Video Serial Visualizer - Copyright (C) 2026  David Nieves
// SPDX-License-Identifier: GPL-3.0-or-later
// Software libre, sin garantia alguna. Ver LICENSE para los terminos completos.

using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using VideoSerialVisualizer.Helpers;
using VideoSerialVisualizer.Localization;

namespace VideoSerialVisualizer.ViewModels;

/// <summary>Tarjeta de un curso en Explorar: nombre, cantidad de grupos/videos, progreso y portada.</summary>
public partial class CourseCardViewModel : ObservableObject
{
    public int Id { get; }
    public int GroupCount { get; }
    public int VideoCount { get; }
    public int WatchedCount { get; }
    public long TotalDurationMs { get; }
    public string? CoverPath { get; }

    [ObservableProperty]
    private string name;

    [ObservableProperty]
    private ImageSource? thumbnailImage;

    /// <summary>Hay un grupo arrastrandose encima: la tarjeta se resalta para indicar que lo recibe.</summary>
    [ObservableProperty]
    private bool isDropTarget;

    public bool IsEmpty => GroupCount == 0;

    public double WatchedPercent => VideoCount > 0 ? WatchedCount / (double)VideoCount * 100.0 : 0;

    public bool AllWatched => VideoCount > 0 && WatchedCount == VideoCount;

    public string WatchedTooltip => string.Format(Loc.I["Explore_WatchedProgress"], WatchedCount, VideoCount);

    /// <summary>"4 grupos · 52 video(s) · 8h 30min", o la invitacion a arrastrar si esta vacio.</summary>
    public string InfoText
    {
        get
        {
            if (IsEmpty)
                return Loc.I["Course_EmptyHint"];

            var parts = new List<string>
            {
                string.Format(Loc.I["Course_GroupCount"], GroupCount),
                $"{VideoCount}{Loc.I["Common_VideosSuffix"]}",
            };
            var duration = DurationFormat.Total(TotalDurationMs);
            if (!string.IsNullOrEmpty(duration))
                parts.Add(duration);
            return string.Join("  ·  ", parts);
        }
    }

    public CourseCardViewModel(int id, string name, int groupCount, int videoCount, int watchedCount, long totalDurationMs, string? coverPath)
    {
        Id = id;
        this.name = name;
        GroupCount = groupCount;
        VideoCount = videoCount;
        WatchedCount = watchedCount;
        TotalDurationMs = totalDurationMs;
        CoverPath = coverPath;
    }

    public async Task LoadThumbnailAsync()
    {
        if (!string.IsNullOrEmpty(CoverPath))
            ThumbnailImage = await ThumbnailLoader.LoadCachedAsync(CoverPath);
    }
}
