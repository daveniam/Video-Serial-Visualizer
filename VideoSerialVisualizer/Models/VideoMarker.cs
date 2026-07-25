// Video Serial Visualizer - Copyright (C) 2026  David Nieves
// SPDX-License-Identifier: GPL-3.0-or-later
// Software libre, sin garantia alguna. Ver LICENSE para los terminos completos.

using System.ComponentModel.DataAnnotations;

namespace VideoSerialVisualizer.Models;

/// <summary>
/// Etiqueta anotada sobre la linea de tiempo de un video (modo animador). El texto admite sintaxis
/// Markdown; se guarda tal cual (crudo) y se renderiza al mostrarlo (ver VideoMarkerViewModel).
/// </summary>
public class VideoMarker
{
    [Key]
    public int Id { get; set; }

    public int VideoId { get; set; }

    public long TimeMs { get; set; }

    [Required]
    public string Texto { get; set; } = string.Empty;

    public Video? Video { get; set; }
}
