using System.IO;
using LibVLCSharp.Shared;
using Microsoft.EntityFrameworkCore;
using VideoSerialVisualizer.Data;
using VideoSerialVisualizer.Models;

namespace VideoSerialVisualizer.Services;

public readonly record struct ScanProgress(string FileName, int Current, int Total);

public class FolderScannerService
{
    private static readonly string[] VideoExtensions = { ".mp4", ".mkv", ".avi", ".mov", ".webm" };

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

        var existingPaths = new HashSet<string>(
            await db.Videos.Select(v => v.RutaAbsoluta).ToListAsync(),
            StringComparer.OrdinalIgnoreCase);

        var newFiles = files.Where(f => !existingPaths.Contains(f)).ToList();

        for (var i = 0; i < newFiles.Count; i++)
        {
            var filePath = newFiles[i];

            progress?.Report(new ScanProgress(Path.GetFileName(filePath), i + 1, newFiles.Count));

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

            var thumbnailPath = await _thumbnailService.GenerateThumbnailAsync(video.Id, filePath, durationMs);
            if (thumbnailPath is not null)
            {
                video.ThumbnailPath = thumbnailPath;
                await db.SaveChangesAsync();
            }
        }

        return addedVideos;
    }

    private async Task<long> GetDurationMsAsync(string filePath)
    {
        using var media = new Media(_libVlc, new Uri(filePath));
        await media.Parse(MediaParseOptions.ParseLocal);
        return media.Duration;
    }
}
