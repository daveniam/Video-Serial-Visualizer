// Video Serial Visualizer - Copyright (C) 2026  David Nieves
// SPDX-License-Identifier: GPL-3.0-or-later
// Software libre, sin garantia alguna. Ver LICENSE para los terminos completos.

using VideoSerialVisualizer.Localization;

namespace VideoSerialVisualizer.Helpers;

public static class DurationFormat
{
    /// <summary>Formatea una duracion total en "Xh Ymin" (o solo minutos si es menos de una hora).
    /// Devuelve vacio si es 0 (todavia sin escanear o desconocida).</summary>
    public static string Total(long totalMs)
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
}
