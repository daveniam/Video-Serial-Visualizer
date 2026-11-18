// Video Serial Visualizer - Copyright (C) 2026  David Nieves
// SPDX-License-Identifier: GPL-3.0-or-later
// Software libre, sin garantia alguna. Ver LICENSE para los terminos completos.

using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace VideoSerialVisualizer.Services;

/// <summary>
/// Quita las OTRAS instalaciones de la app para que nunca queden dos copias a la vez. Hay tres
/// formas de instalarla y cada una vive en un lugar distinto:
///   * Velopack (Setup con auto-actualizacion): %LocalAppData%\VideoSerialVisualizer
///   * Inno Setup (Setup con selector de carpeta): donde el usuario eligio; se registra en
///     HKCU\...\Uninstall\{AppId}_is1 (ver installer\VideoSerialVisualizer.iss)
///   * Install.ps1 (el script de las primeras versiones, sin registro):
///     %LocalAppData%\Programs\VideoSerialVisualizer (y antes %LocalAppData%\Programs\TutorialHub)
///
/// Se llama desde los ganchos de Velopack al instalar y al actualizar (ver Program.cs). El
/// instalador de Inno hace lo mismo del otro lado, en su seccion [Code].
///
/// Solo se borran programas: la biblioteca, el progreso y las miniaturas viven aparte, en
/// %LocalAppData%\TutorialHub, y no se tocan. Todo es "mejor esfuerzo": si una copia esta abierta
/// o un archivo esta bloqueado se deja como esta, y se reintenta en la proxima actualizacion.
/// </summary>
internal static class OtherInstallsCleaner
{
    /// <summary>AppId del instalador de Inno (fijo, ver VideoSerialVisualizer.iss).</summary>
    private const string InnoUninstallKey =
        @"Software\Microsoft\Windows\CurrentVersion\Uninstall\{7F3A9C21-4B6E-4D8A-9F12-2E5C7A9B3D40}_is1";

    public static void RemoveOtherInstalls()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        // Copia del script viejo (y la anterior al cambio de nombre).
        TryRemoveFolderInstall(Path.Combine(localAppData, "Programs", "VideoSerialVisualizer"));
        TryRemoveFolderInstall(Path.Combine(localAppData, "Programs", "TutorialHub"));

        // Copia de Inno Setup.
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(InnoUninstallKey);
            var innoDir = key?.GetValue("InstallLocation") as string
                          ?? key?.GetValue("Inno Setup: App Path") as string;

            if (!string.IsNullOrWhiteSpace(innoDir) && TryRemoveFolderInstall(innoDir))
                Registry.CurrentUser.DeleteSubKeyTree(InnoUninstallKey, throwOnMissingSubKey: false);
        }
        catch
        {
            // mejor esfuerzo
        }
    }

    /// <summary>
    /// Borra una instalacion que es solo una carpeta, junto con sus accesos directos. Devuelve true
    /// si la carpeta ya no existe al terminar. Nunca toca la carpeta de la app que se esta ejecutando.
    /// </summary>
    private static bool TryRemoveFolderInstall(string folder)
    {
        try
        {
            var dir = Normalize(folder);
            if (!Directory.Exists(dir))
                return true;

            // Por si alguna vez el destino coincidiera con esta misma instalacion.
            if (IsSameOrInside(Normalize(AppContext.BaseDirectory), dir))
                return false;

            // Abierta: no se puede borrar (archivos bloqueados). Se reintenta la proxima vez.
            if (IsAnyProcessRunningFrom(dir))
                return false;

            RemoveShortcutsPointingInto(dir);
            Directory.Delete(dir, recursive: true);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Borra los accesos directos del Escritorio y del Menu Inicio que apuntan DENTRO de la carpeta.
    /// Se mira el destino y no el nombre: todas las instalaciones crean un "Video Serial
    /// Visualizer.lnk", y por nombre se borraria tambien el de la instalacion que queda.
    /// </summary>
    private static void RemoveShortcutsPointingInto(string dir)
    {
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.Programs),
        };

        Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType is null)
            return;

        dynamic shell = Activator.CreateInstance(shellType)!;

        foreach (var root in roots)
        {
            if (!Directory.Exists(root))
                continue;

            // Un nivel de subcarpetas alcanza: el grupo de Inno es Menu Inicio\Programs\<app>\.
            var links = Directory.EnumerateFiles(root, "*.lnk", new EnumerationOptions
            {
                RecurseSubdirectories = true,
                MaxRecursionDepth = 1,
                IgnoreInaccessible = true,
            });

            foreach (var link in links)
            {
                try
                {
                    string target = shell.CreateShortcut(link).TargetPath;
                    if (string.IsNullOrEmpty(target) || !IsSameOrInside(Normalize(target), dir))
                        continue;

                    File.Delete(link);

                    // Si era la carpeta del grupo de Inno y quedo vacia, se borra tambien.
                    var parent = Path.GetDirectoryName(link);
                    if (parent is not null && !string.Equals(Normalize(parent), Normalize(root), StringComparison.OrdinalIgnoreCase)
                        && !Directory.EnumerateFileSystemEntries(parent).Any())
                        Directory.Delete(parent);
                }
                catch
                {
                    // un acceso directo que no se puede leer o borrar no frena el resto
                }
            }
        }
    }

    private static bool IsAnyProcessRunningFrom(string dir)
    {
        foreach (var process in Process.GetProcessesByName("VideoSerialVisualizer")
                     .Concat(Process.GetProcessesByName("TutorialHub")))
        {
            try
            {
                var path = process.MainModule?.FileName;
                if (path is not null && IsSameOrInside(Normalize(path), dir))
                    return true;
            }
            catch
            {
                // proceso de otro usuario o que ya termino
            }
            finally
            {
                process.Dispose();
            }
        }

        return false;
    }

    private static string Normalize(string path) =>
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static bool IsSameOrInside(string path, string dir) =>
        string.Equals(path, dir, StringComparison.OrdinalIgnoreCase)
        || path.StartsWith(dir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}
