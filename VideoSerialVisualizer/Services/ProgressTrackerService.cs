// Video Serial Visualizer - Copyright (C) 2026  David Nieves
// SPDX-License-Identifier: GPL-3.0-or-later
// Software libre, sin garantia alguna. Ver LICENSE para los terminos completos.

using Microsoft.EntityFrameworkCore;
using VideoSerialVisualizer.Data;
using VideoSerialVisualizer.Models;

namespace VideoSerialVisualizer.Services;

public class ProgressTrackerService
{
    private const double CompletionThreshold = 0.95;

    public async Task<WatchProgress?> GetProgressAsync(int videoId)
    {
        await using var db = new AppDbContext();
        return await db.Progress.AsNoTracking().FirstOrDefaultAsync(p => p.VideoId == videoId);
    }

    public async Task SaveProgressAsync(int videoId, long positionMs, long durationMs)
    {
        await using var db = new AppDbContext();

        // Si el video ya no esta en la base (p.ej. se quito su carpeta mientras se reproducia, o la
        // fila quedo de un estado inconsistente), no se guarda progreso: insertarlo violaria la FK y
        // tiraria el dialogo de error. Se omite en silencio, no hay nada util que registrar.
        if (!await db.Videos.AnyAsync(v => v.Id == videoId))
            return;

        var progress = await db.Progress.FirstOrDefaultAsync(p => p.VideoId == videoId);
        var completado = durationMs > 0 && positionMs >= durationMs * CompletionThreshold;

        if (progress is null)
        {
            db.Progress.Add(new WatchProgress
            {
                VideoId = videoId,
                PosicionMs = positionMs,
                Completado = completado,
                UltimaVezVisto = DateTime.Now
            });
        }
        else
        {
            progress.PosicionMs = positionMs;
            progress.Completado = completado;
            progress.UltimaVezVisto = DateTime.Now;
        }

        await db.SaveChangesAsync();
    }
}
