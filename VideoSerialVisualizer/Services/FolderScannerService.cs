// Video Serial Visualizer - Copyright (C) 2026  David Nieves
// SPDX-License-Identifier: GPL-3.0-or-later
// Software libre, sin garantia alguna. Ver LICENSE para los terminos completos.

using System.IO;
using LibVLCSharp.Shared;
using Microsoft.EntityFrameworkCore;
using VideoSerialVisualizer.Data;
using VideoSerialVisualizer.Models;

namespace VideoSerialVisualizer.Services;

public readonly record struct ScanProgress(string FileName, int Current, int Total);

public class FolderScannerService
{
    private static readonly string[] VideoExtensions =
    {
        ".mp4", ".m4v", ".mkv", ".avi", ".mov", ".webm", ".flv", ".wmv",
        ".mpg", ".mpeg", ".ts", ".m2ts", ".3gp", ".ogv"
    };

    private readonly LibVLC _libVlc;
    private readonly ThumbnailService _thumbnailService;

    public FolderScannerService(LibVLC libVlc, ThumbnailService thumbnailService)
    {
        _libVlc = libVlc;
        _thumbnailService = thumbnailService;
    }

    public async Task<List<Video>> ScanFolderAsync(string folderPath, IProgress<ScanProgress>? progress = null)
    {
        var addedVideos = new List<Video>();

        var files = Directory.EnumerateFiles(folderPath, "*.*", SearchOption.AllDirectories)
            .Where(f => VideoExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .ToList();

        await using var db = new AppDbContext();

        var existingByPath = (await db.Videos.ToListAsync())
            .ToDictionary(v => v.RutaAbsoluta, StringComparer.OrdinalIgnoreCase);

        var newFiles = files.Where(f => !existingByPath.ContainsKey(f)).ToList();

        // Videos ya agregados a los que les falta la miniatura O quedaron con duracion 0 (Media.Parse
        // fallo en el primer escaneo): re-escanear la carpeta reintenta. Antes se salteaban siempre,
        // asi que un fallo quedaba para siempre.
        var needThumbnail = files
            .Where(f => existingByPath.TryGetValue(f, out var v) && (!HasThumbnail(v) || v.DuracionMs <= 0))
            .Select(f => existingByPath[f])
            .ToList();

        var total = newFiles.Count + needThumbnail.Count;
        var done = 0;

        foreach (var filePath in newFiles)
        {
            progress?.Report(new ScanProgress(Path.GetFileName(filePath), ++done, total));

            var durationMs = await GetDurationMsAsync(filePath);

            var video = new Video
            {
                RutaAbsoluta = filePath,
                NombreArchivo = Path.GetFileName(filePath),
                CarpetaOrigen = folderPath,
                FechaAgregado = DateTime.Now,
                DuracionMs = durationMs
            };

            db.Videos.Add(video);
            await db.SaveChangesAsync();
            addedVideos.Add(video);

            var result = await _thumbnailService.GenerateThumbnailAsync(video.Id, filePath);
            ApplyThumbnailResult(video, result);
            await db.SaveChangesAsync();
        }

        foreach (var video in needThumbnail)
        {
            progress?.Report(new ScanProgress(video.NombreArchivo, ++done, total));

            var result = await _thumbnailService.GenerateThumbnailAsync(video.Id, video.RutaAbsoluta);
            ApplyThumbnailResult(video, result);
            await db.SaveChangesAsync();
        }

        return addedVideos;
    }

    /// <summary>Vuelca en el video la miniatura y la duracion observada durante la captura. La
    /// duracion del reproductor es mas confiable que la de Media.Parse, asi que corrige el 0 que
    /// este ultimo deja en algunos MP4.</summary>
    private static void ApplyThumbnailResult(Video video, ThumbnailService.ThumbnailResult result)
    {
        if (result.ThumbnailPath is not null)
            video.ThumbnailPath = result.ThumbnailPath;

        if (result.DurationMs > 0)
            video.DuracionMs = result.DurationMs;
    }

    /// <summary>El video tiene una miniatura y su archivo sigue existiendo.</summary>
    private static bool HasThumbnail(Video v)
        => !string.IsNullOrEmpty(v.ThumbnailPath) && File.Exists(v.ThumbnailPath);

    private async Task<long> GetDurationMsAsync(string filePath)
    {
        using var media = new Media(_libVlc, new Uri(filePath));
        await media.Parse(MediaParseOptions.ParseLocal);
        return media.Duration;
    }
}
