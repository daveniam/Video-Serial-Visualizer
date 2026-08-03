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

    /// <summary>
    /// Portada elegida a mano para el grupo (ruta absoluta a un JPG en la carpeta de miniaturas).
    /// La fija el usuario con clic derecho sobre la barra del reproductor. Si es null, la caratula
    /// cae al comportamiento por defecto (la miniatura del ultimo video del grupo).
    /// </summary>
    public string? CoverImagePath { get; set; }

    public Category? Category { get; set; }
}
