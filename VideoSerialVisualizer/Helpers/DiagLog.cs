// Video Serial Visualizer - Copyright (C) 2026  David Nieves
// SPDX-License-Identifier: GPL-3.0-or-later
// Software libre, sin garantia alguna. Ver LICENSE para los terminos completos.

using System.IO;

namespace VideoSerialVisualizer.Helpers;

/// <summary>
/// EXPERIMENTAL (rama experimental/d3dimage): registro de diagnostico del render de video, para
/// investigar fallas que solo aparecen en la app real (no en el arnes de pruebas). Escribe en
/// %LocalAppData%\TutorialHub\d3dimage-diag.log; se reinicia en cada arranque de la app.
/// </summary>
internal static class DiagLog
{
    private static readonly object Sync = new();
    private static readonly System.Diagnostics.Stopwatch Clock = System.Diagnostics.Stopwatch.StartNew();
    private static readonly string? FilePath = CreateFile();

    private static string? CreateFile()
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TutorialHub");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "d3dimage-diag.log");
            File.WriteAllText(path, $"=== arranque {DateTime.Now:yyyy-MM-dd HH:mm:ss} ==={Environment.NewLine}");
            return path;
        }
        catch
        {
            return null;
        }
    }

    public static void Write(string message)
    {
        if (FilePath is null)
            return;

        try
        {
            var line = $"{Clock.ElapsedMilliseconds,7} ms [hilo {Environment.CurrentManagedThreadId,2}] {message}{Environment.NewLine}";
            lock (Sync)
                File.AppendAllText(FilePath, line);
        }
        catch
        {
            // el diagnostico nunca debe romper la reproduccion
        }
    }
}
