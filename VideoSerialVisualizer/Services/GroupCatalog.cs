// Video Serial Visualizer - Copyright (C) 2026  David Nieves
// SPDX-License-Identifier: GPL-3.0-or-later
// Software libre, sin garantia alguna. Ver LICENSE para los terminos completos.

using System.IO;
using Microsoft.EntityFrameworkCore;
using VideoSerialVisualizer.Data;
using VideoSerialVisualizer.Helpers;
using VideoSerialVisualizer.Models;
using VideoSerialVisualizer.ViewModels;

namespace VideoSerialVisualizer.Services;

/// <summary>
/// Arma las tarjetas de todos los grupos (una por carpeta con videos) con su conteo, duracion,
/// progreso, portada, categorias y curso. Lo comparten Explorar y la vista de un curso.
/// </summary>
public static class GroupCatalog
{
    public static async Task<List<FolderCardViewModel>> LoadGroupCardsAsync(AppDbContext db)
    {
        // El conteo y la duracion total por carpeta se resuelven con GROUP BY en SQL: una fila por
        // grupo (con COUNT y SUM), en vez de traer la tabla Videos entera a memoria.
        var folderCounts = await db.Videos.AsNoTracking()
            .GroupBy(v => v.CarpetaOrigen)
            .Select(g => new { FolderPath = g.Key, Count = g.Count(), TotalMs = g.Sum(v => v.DuracionMs) })
            .ToListAsync();

        // Cuantos videos completados tiene cada carpeta, para la linea de progreso del grupo.
        var watchedByFolder = await db.Videos.AsNoTracking()
            .Join(db.Progress.AsNoTracking().Where(p => p.Completado), v => v.Id, p => p.VideoId, (v, p) => v.CarpetaOrigen)
            .GroupBy(f => f)
            .Select(g => new { FolderPath = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.FolderPath, g => g.Count);

        // Para la caratula solo hacen falta los videos que TIENEN miniatura, y solo tres columnas
        // (no la entidad completa): la eleccion del "ultimo" usa orden natural, que es logica C#.
        var thumbnailCandidates = await db.Videos.AsNoTracking()
            .Where(v => v.ThumbnailPath != null && v.ThumbnailPath != "")
            .Select(v => new { v.CarpetaOrigen, v.NombreArchivo, v.ThumbnailPath })
            .ToListAsync();

        var thumbnailsByFolder = thumbnailCandidates
            .GroupBy(v => v.CarpetaOrigen)
            .ToDictionary(
                g => g.Key,
                // La caratula usa el ULTIMO video de la carpeta (orden natural): suele ser
                // el resultado final del proceso que ensena el tutorial.
                g => g.OrderBy(v => v.NombreArchivo, NaturalStringComparer.Instance).Last().ThumbnailPath);

        var categories = await db.FolderCategories.AsNoTracking().ToDictionaryAsync(c => c.FolderPath);

        // Categorias asignadas a cada grupo (puede haber varias). Se agrupan por carpeta para el
        // filtro de Explorar.
        var tagsByFolder = (await db.FolderTags.AsNoTracking().ToListAsync())
            .GroupBy(t => t.FolderPath, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyCollection<int>)g.Select(t => t.CategoryId).ToHashSet(),
                StringComparer.OrdinalIgnoreCase);

        return folderCounts
            .OrderBy(g => g.FolderPath, NaturalStringComparer.Instance)
            .Select(group =>
            {
                categories.TryGetValue(group.FolderPath, out var category);
                thumbnailsByFolder.TryGetValue(group.FolderPath, out var thumbnailPath);
                tagsByFolder.TryGetValue(group.FolderPath, out var categoryIds);

                return new FolderCardViewModel(
                    group.FolderPath, group.Count, group.TotalMs,
                    EffectiveCover(category?.CoverImagePath, thumbnailPath),
                    category?.DisplayName, category?.Favorito ?? false,
                    categoryIds ?? Array.Empty<int>(), watchedByFolder.GetValueOrDefault(group.FolderPath))
                {
                    CourseId = category?.CourseId,
                    CourseOrder = category?.CourseOrder ?? 0,
                };
            })
            .ToList();
    }

    /// <summary>
    /// Portada elegida a mano (clic derecho en la barra del reproductor) si existe y no salio negra
    /// (ver ThumbnailService.IsBlankImage); si no, <paramref name="fallback"/>.
    /// </summary>
    public static string? EffectiveCover(string? chosenCoverPath, string? fallback)
        => !string.IsNullOrEmpty(chosenCoverPath) && File.Exists(chosenCoverPath)
           && !ThumbnailService.IsBlankImage(chosenCoverPath)
            ? chosenCoverPath
            : fallback;

    /// <summary>Ficha de metadatos del grupo; la crea si todavia no existe (se crean a demanda).</summary>
    public static async Task<FolderCategory> GetOrCreateFolderEntryAsync(AppDbContext db, string folderPath)
    {
        var entry = await db.FolderCategories.FirstOrDefaultAsync(f => f.FolderPath == folderPath);
        if (entry is not null)
            return entry;

        entry = db.FolderCategories.Local.FirstOrDefault(f => f.FolderPath == folderPath);
        if (entry is not null)
            return entry;

        entry = new FolderCategory { FolderPath = folderPath };
        db.FolderCategories.Add(entry);
        return entry;
    }

    /// <summary>
    /// Mete los grupos en el curso, al final y en el orden dado. Un grupo que estaba en otro curso
    /// se muda (solo puede estar en uno).
    /// </summary>
    public static async Task AddGroupsToCourseAsync(int courseId, IEnumerable<string> folderPaths)
    {
        await using var db = new AppDbContext();

        var nextOrder = await db.FolderCategories
            .Where(f => f.CourseId == courseId)
            .Select(f => (int?)f.CourseOrder)
            .MaxAsync() ?? -1;

        foreach (var path in folderPaths)
        {
            var entry = await GetOrCreateFolderEntryAsync(db, path);
            if (entry.CourseId == courseId)
                continue; // ya estaba: conserva su lugar

            entry.CourseId = courseId;
            entry.CourseOrder = ++nextOrder;
        }

        await db.SaveChangesAsync();
    }

    /// <summary>Todos los videos del curso en orden: grupo por grupo (orden del curso) y, dentro de
    /// cada grupo, en orden natural. Es la lista de reproduccion de "Continuar curso".</summary>
    public static async Task<List<Video>> GetCoursePlaylistAsync(int courseId)
    {
        await using var db = new AppDbContext();

        var groups = await db.FolderCategories.AsNoTracking()
            .Where(f => f.CourseId == courseId)
            .OrderBy(f => f.CourseOrder)
            .Select(f => f.FolderPath)
            .ToListAsync();

        var videos = await db.Videos.AsNoTracking()
            .Where(v => groups.Contains(v.CarpetaOrigen))
            .Include(v => v.Progress)
            .ToListAsync();

        var byFolder = videos.ToLookup(v => v.CarpetaOrigen, StringComparer.OrdinalIgnoreCase);

        return groups
            .SelectMany(g => byFolder[g].OrderBy(v => v.NombreArchivo, NaturalStringComparer.Instance))
            .ToList();
    }
}
