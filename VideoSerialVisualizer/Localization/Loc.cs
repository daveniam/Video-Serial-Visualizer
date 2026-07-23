// Video Serial Visualizer - Copyright (C) 2026  David Nieves
// SPDX-License-Identifier: GPL-3.0-or-later
// Software libre, sin garantia alguna. Ver LICENSE para los terminos completos.

using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json;

namespace VideoSerialVisualizer.Localization;

/// <summary>Un idioma disponible en la interfaz.</summary>
public sealed record LanguageOption(string Code, string NativeName);

/// <summary>
/// Diccionario de textos de la interfaz. Se usa desde XAML con la extension {loc:Tr Clave}.
///
/// Es un singleton que notifica cambios sobre su indexador: al cambiar de idioma, todos los
/// bindings se reevaluan solos y la interfaz se traduce sin reiniciar la aplicacion.
///
/// Las traducciones viven en archivos JSON embebidos (Localization/*.json). Se eligio JSON y no
/// .resx porque es mucho mas simple de editar para quien quiera contribuir una traduccion.
/// </summary>
public sealed class Loc : INotifyPropertyChanged
{
    /// <summary>Idioma de referencia: contiene TODAS las claves y sirve de respaldo.</summary>
    private const string FallbackCode = "en";

    public static Loc I { get; } = new();

    /// <summary>Idiomas ofrecidos, cada uno escrito en su propia lengua.</summary>
    public static IReadOnlyList<LanguageOption> Available { get; } = new[]
    {
        new LanguageOption("en",      "English"),
        new LanguageOption("es",      "Español"),
        new LanguageOption("zh-Hans", "简体中文"),
        new LanguageOption("ja",      "日本語"),
        new LanguageOption("de",      "Deutsch"),
        new LanguageOption("ru",      "Русский"),
        new LanguageOption("fr",      "Français"),
    };

    private Dictionary<string, string> _strings = new();
    private Dictionary<string, string> _fallback = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    public string CurrentCode { get; private set; } = FallbackCode;

    private Loc()
    {
        _fallback = Load(FallbackCode);
        _strings = _fallback;
    }

    /// <summary>
    /// Texto para una clave. Si falta en el idioma actual cae al ingles, y si tampoco esta,
    /// devuelve la clave entre corchetes: es preferible ver "[Clave_Faltante]" en pantalla que
    /// un hueco vacio, porque delata la omision durante las pruebas.
    /// </summary>
    public string this[string key]
    {
        get
        {
            if (_strings.TryGetValue(key, out var value))
                return value;
            if (_fallback.TryGetValue(key, out var fallback))
                return fallback;
            return $"[{key}]";
        }
    }

    public void SetLanguage(string code)
    {
        if (string.IsNullOrWhiteSpace(code) || code == CurrentCode)
            return;

        if (!Available.Any(l => l.Code == code))
            return;

        _strings = code == FallbackCode ? _fallback : Load(code);
        CurrentCode = code;

        // Cadena vacia = "todas las propiedades cambiaron": refresca cada binding del indexador.
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    }

    /// <summary>
    /// Idioma sugerido segun la configuracion de Windows. Se compara primero el codigo completo
    /// (p. ej. "zh-Hans") y despues solo el idioma ("es" para "es-AR"), para que un usuario
    /// argentino o mexicano reciba español sin tener que elegirlo.
    /// </summary>
    public static string DetectSystemLanguage()
    {
        var culture = CultureInfo.CurrentUICulture;

        foreach (var candidate in new[] { culture.Name, culture.TwoLetterISOLanguageName })
        {
            var match = Available.FirstOrDefault(l =>
                string.Equals(l.Code, candidate, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
                return match.Code;
        }

        // El chino simplificado llega como zh-CN, zh-SG o zh-Hans-*; todos van al mismo archivo.
        if (culture.TwoLetterISOLanguageName.Equals("zh", StringComparison.OrdinalIgnoreCase))
            return "zh-Hans";

        return FallbackCode;
    }

    private static Dictionary<string, string> Load(string code)
    {
        // Los JSON van embebidos en el ejecutable: no hay archivos sueltos que se puedan perder
        // ni rutas que resolver en tiempo de ejecucion.
        var resourceName = $"VideoSerialVisualizer.Localization.{code}.json";

        try
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
            if (stream is null)
                return new Dictionary<string, string>();

            using var reader = new StreamReader(stream);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(reader.ReadToEnd())
                   ?? new Dictionary<string, string>();
        }
        catch
        {
            // Un archivo de idioma corrupto no debe impedir que la app abra: se cae al respaldo.
            return new Dictionary<string, string>();
        }
    }
}
