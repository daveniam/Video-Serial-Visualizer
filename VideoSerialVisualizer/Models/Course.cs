// Video Serial Visualizer - Copyright (C) 2026  David Nieves
// SPDX-License-Identifier: GPL-3.0-or-later
// Software libre, sin garantia alguna. Ver LICENSE para los terminos completos.

using System.ComponentModel.DataAnnotations;

namespace VideoSerialVisualizer.Models;

/// <summary>
/// Un curso: agrupa varios grupos (carpetas) en un orden propio. Cada grupo puede estar en UN solo
/// curso; la pertenencia y el orden viven en <see cref="FolderCategory.CourseId"/> y
/// <see cref="FolderCategory.CourseOrder"/>. Borrar un curso no borra sus grupos: vuelven a quedar
/// sueltos en Explorar.
/// </summary>
public class Course
{
    [Key]
    public int Id { get; set; }

    [Required]
    public string Name { get; set; } = string.Empty;

    /// <summary>Portada elegida a mano (ruta a una miniatura). Si es null, o el archivo ya no esta,
    /// se usa la portada del primer grupo del curso.</summary>
    public string? CoverImagePath { get; set; }

    public DateTime FechaCreado { get; set; } = DateTime.Now;
}
