// Video Serial Visualizer - Copyright (C) 2026  David Nieves
// SPDX-License-Identifier: GPL-3.0-or-later
// Software libre, sin garantia alguna. Ver LICENSE para los terminos completos.

using System.IO;
using LibVLCSharp.Shared;
using Microsoft.EntityFrameworkCore;
using VideoSerialVisualizer.Data;
using VideoSerialVisualizer.Helpers;
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

    // LibVLC se resuelve de forma perezosa (se inicializa en segundo plano al arrancar): el primer
    // escaneo espera a que este listo. Una vez resuelto queda cacheado.
    private readonly Func<Task<LibVLC>> _libVlcProvider;
    private LibVLC? _libVlc;
    private readonly ThumbnailService _thumbnailService;

    public FolderScannerService(Func<Task<LibVLC>> libVlcProvider, ThumbnailService thumbnailService)
    {
        _libVlcProvider = libVlcProvider;
        _thumbnailService = thumbnailService;
    }

    /// <summary>
    /// Importa una carpeta armando UN GRUPO POR SUBCARPETA (lo que pide un curso ya organizado en
    /// carpetas: "01_Intro", "02_Bases"...). Cada subcarpeta con videos (a cualquier profundidad) es
    /// un grupo; si la carpeta elegida tiene ademas videos sueltos en su raiz, esos forman otro grupo.
    /// Sin subcarpetas con videos, la carpeta entera es un solo grupo, como en "Agregar carpeta".
    ///
    /// Los videos que ya estaban cargados en otro grupo (p.ej. porque antes se agrego la carpeta
    /// madre como un unico grupo) pasan al grupo de su subcarpeta.
    /// </summary>
    /// <returns>Las carpetas de los grupos resultantes, en orden natural.</returns>
    public async Task<List<string>> ImportAsSubfolderGroupsAsync(string folderPath, IProgress<ScanProgress>? progress = null)
    {
        var subfolders = Directory.EnumerateDirectories(folderPath)
            .Where(d => !IsHidden(d) && ContainsVideos(d, SearchOption.AllDirectories))
            .OrderBy(d => d, NaturalStringComparer.Instance)
            .ToList();

        var groups = new List<(string Path, bool Recursive)>();
        if (subfolders.Count == 0)
        {
            groups.Add((folderPath, true));
        }
        else
        {
            if (ContainsVideos(folderPath, SearchOption.TopDirectoryOnly))
                groups.Add((folderPath, false));
            groups.AddRange(subfolders.Select(s => (s, true)));
        }

        var result = new List<string>();
        foreach (var (path, recursive) in groups)
        {
            await ScanFolderAsync(path, progress, recursive, claimFromOtherGroups: true);
            result.Add(path);
        }

        return result;
    }

    private static bool ContainsVideos(string folder, SearchOption option)
    {
        try
        {
            return Directory.EnumerateFiles(folder, "*.*", option)
                .Any(f => VideoExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()) && !IsAppleDoubleSidecar(f));
        }
        catch
        {
            return false; // sin permiso de lectura, etc.
        }
    }

    private static bool IsHidden(string folder)
    {
        try
        {
            var name = Path.GetFileName(folder);
            return name.StartsWith('.') || (File.GetAttributes(folder) & (FileAttributes.Hidden | FileAttributes.System)) != 0;
        }
        catch
        {
            return true;
        }
    }

    /// <param name="recursive">Incluir los videos de las subcarpetas (lo normal) o solo los de la raiz.</param>
    /// <param name="claimFromOtherGroups">Los videos de esta carpeta que ya estaban cargados en OTRO
    /// grupo pasan a este (ver <see cref="ImportAsSubfolderGroupsAsync"/>).</param>
    public async Task<List<Video>> ScanFolderAsync(string folderPath, IProgress<ScanProgress>? progress = null,
        bool recursive = true, bool claimFromOtherGroups = false)
    {
        _libVlc ??= await _libVlcProvider();
        var addedVideos = new List<Video>();

        var allFiles = Directory.EnumerateFiles(folderPath, "*.*", recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
            .Where(f => VideoExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .ToList();

        // "._NombreOriginal.mp4": metadata "AppleDouble" que macOS deja junto al archivo real al
        // copiar a un disco no-Mac (exFAT/NTFS). Pesan unos cientos de bytes y no tienen video
        // adentro, asi que Media.Parse siempre da duracion 0 y nunca van a poder tener miniatura.
        // Se descartan del escaneo, y si ya habian quedado cargados de un escaneo previo se borran.
        var files = allFiles.Where(f => !IsAppleDoubleSidecar(f)).ToList();
        var sidecarFiles = allFiles.Where(IsAppleDoubleSidecar).ToList();

        await using var db = new AppDbContext();

        var existingByPath = (await db.Videos.ToListAsync())
            .ToDictionary(v => v.RutaAbsoluta, StringComparer.OrdinalIgnoreCase);

        if (sidecarFiles.Count > 0)
        {
            var junkVideos = sidecarFiles
                .Where(f => existingByPath.ContainsKey(f))
                .Select(f => existingByPath[f])
                .ToList();

            if (junkVideos.Count > 0)
            {
                db.Videos.RemoveRange(junkVideos);
                await db.SaveChangesAsync();
                foreach (var junk in junkVideos)
                    existingByPath.Remove(junk.RutaAbsoluta);
            }
        }

        if (claimFromOtherGroups)
        {
            var claimed = files
                .Where(f => existingByPath.TryGetValue(f, out var v)
                            && !string.Equals(v.CarpetaOrigen, folderPath, StringComparison.OrdinalIgnoreCase))
                .Select(f => existingByPath[f])
                .ToList();

            foreach (var video in claimed)
                video.CarpetaOrigen = folderPath;

            if (claimed.Count > 0)
                await db.SaveChangesAsync();
        }

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
        {
            video.ThumbnailPath = result.ThumbnailPath;
            // Se regenera en el mismo archivo ({id}.jpg): la imagen vieja no debe quedar en cache.
            Helpers.ThumbnailLoader.Invalidate(result.ThumbnailPath);
        }

        if (result.DurationMs > 0)
            video.DuracionMs = result.DurationMs;
    }

    /// <summary>El video tiene una miniatura, su archivo sigue existiendo y no es una captura negra.
    /// Las negras salian en videos con cuadros clave muy separados (ver ThumbnailService): asi se
    /// regeneran solas en el proximo escaneo.</summary>
    private static bool HasThumbnail(Video v)
        => !string.IsNullOrEmpty(v.ThumbnailPath) && File.Exists(v.ThumbnailPath)
           && !ThumbnailService.IsBlankImage(v.ThumbnailPath);

    /// <summary>Detecta el metadata "AppleDouble" que macOS deja junto a cada archivo real al copiar
    /// a un disco no-Mac: mismo nombre con el prefijo "._", sin video adentro.</summary>
    private static bool IsAppleDoubleSidecar(string filePath)
        => Path.GetFileName(filePath).StartsWith("._", StringComparison.Ordinal);

    private async Task<long> GetDurationMsAsync(string filePath)
    {
        using var media = new Media(_libVlc!, new Uri(filePath));
        await media.Parse(MediaParseOptions.ParseLocal);
        return media.Duration;
    }
}
