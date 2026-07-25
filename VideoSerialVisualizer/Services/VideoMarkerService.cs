// Video Serial Visualizer - Copyright (C) 2026  David Nieves
// SPDX-License-Identifier: GPL-3.0-or-later
// Software libre, sin garantia alguna. Ver LICENSE para los terminos completos.

using Microsoft.EntityFrameworkCore;
using VideoSerialVisualizer.Data;
using VideoSerialVisualizer.Models;

namespace VideoSerialVisualizer.Services;

public class VideoMarkerService
{
    public async Task<List<VideoMarker>> GetMarkersAsync(int videoId)
    {
        await using var db = new AppDbContext();
        return await db.Markers
            .AsNoTracking()
            .Where(m => m.VideoId == videoId)
            .OrderBy(m => m.TimeMs)
            .ToListAsync();
    }

    public async Task<VideoMarker> AddMarkerAsync(int videoId, long timeMs, string texto)
    {
        await using var db = new AppDbContext();
        var marker = new VideoMarker { VideoId = videoId, TimeMs = timeMs, Texto = texto };
        db.Markers.Add(marker);
        await db.SaveChangesAsync();
        return marker;
    }

    public async Task UpdateMarkerTextAsync(int markerId, string texto)
    {
        await using var db = new AppDbContext();
        var marker = await db.Markers.FirstOrDefaultAsync(m => m.Id == markerId);
        if (marker is null)
            return;

        marker.Texto = texto;
        await db.SaveChangesAsync();
    }

    public async Task DeleteMarkerAsync(int markerId)
    {
        await using var db = new AppDbContext();
        var marker = await db.Markers.FirstOrDefaultAsync(m => m.Id == markerId);
        if (marker is null)
            return;

        db.Markers.Remove(marker);
        await db.SaveChangesAsync();
    }
}
