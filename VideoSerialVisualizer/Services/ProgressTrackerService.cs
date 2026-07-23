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
