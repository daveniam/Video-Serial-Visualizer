// Video Serial Visualizer - Copyright (C) 2026  David Nieves
// SPDX-License-Identifier: GPL-3.0-or-later
// Software libre, sin garantia alguna. Ver LICENSE para los terminos completos.

using System.IO;
using System.Text.Json;
using VideoSerialVisualizer.Data;

namespace VideoSerialVisualizer.Services;

/// <summary>
/// Preferencias de la aplicacion, guardadas en un JSON junto a la base de datos.
///
/// Se usa un archivo aparte y no una tabla porque son ajustes de interfaz: hacen falta ANTES de
/// abrir la base (el idioma se aplica en la pantalla de carga) y no tienen relacion con la
/// biblioteca de videos.
/// </summary>
public sealed class AppSettings
{
    /// <summary>Codigo de idioma elegido, o null para seguir el del sistema.</summary>
    public string? Language { get; set; }

    /// <summary>
    /// Modo animador: agrega al reproductor las herramientas de estudio cuadro a cuadro (paso a
    /// fotograma, bucle, seleccion de segmento y etiquetas en la linea de tiempo). Apagado por
    /// defecto para que la interfaz habitual no cambie a quien no lo necesita.
    /// </summary>
    public bool AnimatorModeEnabled { get; set; }

    /// <summary>
    /// Opacidad de la ventana en modo animador, 20 a 100. Se guarda entre sesiones porque quien
    /// calca sobre una referencia suele trabajar siempre con el mismo nivel. El piso de 20 es a
    /// proposito: con menos, la ventana se vuelve tan invisible que cuesta encontrarla para
    /// devolverla a su estado normal.
    /// </summary>
    public double AnimatorWindowOpacityPercent { get; set; } = 100;

    private static string FilePath => Path.Combine(AppDbContext.DatabaseDirectory, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return new AppSettings();

            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch
        {
            // Un archivo corrupto no debe impedir abrir la app: se vuelve a los valores por defecto.
            return new AppSettings();
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(AppDbContext.DatabaseDirectory);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // Sin permisos o disco lleno: se pierde la preferencia, pero la app sigue funcionando.
        }
    }
}
