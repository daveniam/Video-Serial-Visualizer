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
