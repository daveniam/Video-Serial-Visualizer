// Video Serial Visualizer - Copyright (C) 2026  David Nieves
// SPDX-License-Identifier: GPL-3.0-or-later
// Software libre, sin garantia alguna. Ver LICENSE para los terminos completos.

namespace VideoSerialVisualizer.Models;

/// <summary>Una velocidad de reproduccion disponible (p. ej. 1.5x).</summary>
public sealed record PlaybackSpeedOption(float Rate, string Label)
{
    /// <summary>Velocidades ofrecidas, de mas lenta a mas rapida. 1x es la normal.</summary>
    public static readonly IReadOnlyList<PlaybackSpeedOption> All = new[]
    {
        new PlaybackSpeedOption(0.5f,  "0.5x"),
        new PlaybackSpeedOption(0.75f, "0.75x"),
        new PlaybackSpeedOption(1.0f,  "1x"),
        new PlaybackSpeedOption(1.25f, "1.25x"),
        new PlaybackSpeedOption(1.5f,  "1.5x"),
        new PlaybackSpeedOption(1.75f, "1.75x"),
        new PlaybackSpeedOption(2.0f,  "2x"),
    };

    public static PlaybackSpeedOption Normal => All[2];
}
