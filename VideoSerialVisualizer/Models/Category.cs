// Video Serial Visualizer - Copyright (C) 2026  David Nieves
// SPDX-License-Identifier: GPL-3.0-or-later
// Software libre, sin garantia alguna. Ver LICENSE para los terminos completos.

using System.ComponentModel.DataAnnotations;

namespace VideoSerialVisualizer.Models;

/// <summary>
/// Etiqueta personalizada creada desde Configuración (ej. "Blender", "ZBrush") que se puede
/// asignar a uno o mas grupos de carpetas. No confundir con <see cref="FolderCategory"/>, que
/// guarda el nombre visual/favorito de una carpeta puntual.
/// </summary>
public class Category
{
    [Key]
    public int Id { get; set; }

    [Required]
    public string Name { get; set; } = string.Empty;
}
