// Video Serial Visualizer - Copyright (C) 2026  David Nieves
// SPDX-License-Identifier: GPL-3.0-or-later
// Software libre, sin garantia alguna. Ver LICENSE para los terminos completos.

using System.ComponentModel.DataAnnotations;

namespace VideoSerialVisualizer.Models;

/// <summary>
/// Enlace muchos-a-muchos entre un grupo de carpetas (por su <see cref="FolderPath"/>) y una
/// <see cref="Category"/>. Un grupo puede tener varias categorias: una fila por cada asignacion.
/// Reemplaza al viejo <c>FolderCategory.CategoryId</c> (una sola categoria por grupo), que queda
/// como columna vestigial migrada a esta tabla la primera vez.
/// </summary>
public class FolderTag
{
    [Key]
    public int Id { get; set; }

    [Required]
    public string FolderPath { get; set; } = string.Empty;

    public int CategoryId { get; set; }

    public Category? Category { get; set; }
}
