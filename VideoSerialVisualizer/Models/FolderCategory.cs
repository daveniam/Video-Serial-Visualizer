// Video Serial Visualizer - Copyright (C) 2026  David Nieves
// SPDX-License-Identifier: GPL-3.0-or-later
// Software libre, sin garantia alguna. Ver LICENSE para los terminos completos.

using System.ComponentModel.DataAnnotations;

namespace VideoSerialVisualizer.Models;

/// <summary>
/// Ajustes por carpeta agregada a la biblioteca: nombre visual, favorito y categoria asignada.
/// La carpeta real en disco (Video.CarpetaOrigen) nunca se modifica ni se renombra.
/// No confundir con <see cref="Category"/>, que es la etiqueta en si (creada desde Configuración).
/// </summary>
public class FolderCategory
{
    [Key]
    public int Id { get; set; }

    [Required]
    public string FolderPath { get; set; } = string.Empty;

    public string? DisplayName { get; set; }

    public bool Favorito { get; set; }

    public int? CategoryId { get; set; }

    public Category? Category { get; set; }
}
