// Video Serial Visualizer - Copyright (C) 2026  David Nieves
// SPDX-License-Identifier: GPL-3.0-or-later
// Software libre, sin garantia alguna. Ver LICENSE para los terminos completos.

namespace VideoSerialVisualizer.Helpers;

public static class TimeFormatter
{
    public static string Format(double ms)
    {
        var time = TimeSpan.FromMilliseconds(Math.Max(0, ms));
        return time.Hours > 0
            ? time.ToString(@"hh\:mm\:ss")
            : time.ToString(@"mm\:ss");
    }
}
