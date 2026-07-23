// Video Serial Visualizer - Copyright (C) 2026  David Nieves
// SPDX-License-Identifier: GPL-3.0-or-later
// Software libre, sin garantia alguna. Ver LICENSE para los terminos completos.

using System.Windows.Data;
using System.Windows.Markup;

namespace VideoSerialVisualizer.Localization;

/// <summary>
/// Extension de marcado para traducir desde XAML:
///
///     Text="{loc:Tr Explore_Title}"
///
/// Devuelve un binding al indexador de <see cref="Loc"/>, no un texto fijo. Por eso al cambiar de
/// idioma la interfaz se actualiza sola, sin reiniciar.
/// </summary>
public sealed class TrExtension : MarkupExtension
{
    public string Key { get; set; } = string.Empty;

    public TrExtension() { }

    public TrExtension(string key) => Key = key;

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var binding = new Binding($"[{Key}]")
        {
            Source = Loc.I,
            Mode = BindingMode.OneWay
        };

        return binding.ProvideValue(serviceProvider);
    }
}
