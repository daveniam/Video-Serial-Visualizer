// Video Serial Visualizer - Copyright (C) 2026  David Nieves
// SPDX-License-Identifier: GPL-3.0-or-later
// Software libre, sin garantia alguna. Ver LICENSE para los terminos completos.

using System.IO;

namespace VideoSerialVisualizer.Helpers;

/// <summary>
/// Registro de diagnostico del render de video, para investigar fallas que solo aparecen en un
/// equipo real (perdida de la GPU al bloquear la sesion, video invisible, parpadeo...). Fue lo que
/// permitio encontrar esos errores durante la migracion a D3DImage.
///
/// Apagado por defecto: solo funciona arrancando la app con la variable de entorno VSV_DIAG=1, y
/// en ese caso ademas se muestra sobre el video el cartel con las mediciones del renderer. Escribe
/// en %LocalAppData%\TutorialHub\d3dimage-diag.log, que se reinicia en cada arranque.
/// </summary>
internal static class DiagLog
{
    /// <summary>El diagnostico esta activado (VSV_DIAG=1).</summary>
    public static bool IsEnabled { get; } =
        Environment.GetEnvironmentVariable("VSV_DIAG")?.Trim() == "1";

    private static readonly object Sync = new();
    private static readonly System.Diagnostics.Stopwatch Clock = System.Diagnostics.Stopwatch.StartNew();
    private static readonly string? FilePath = IsEnabled ? CreateFile() : null;

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
