using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace VideoSerialVisualizer.Models;

public class Video
{
    [Key]
    public int Id { get; set; }

    [Required]
    public string RutaAbsoluta { get; set; } = string.Empty;

    [Required]
    public string NombreArchivo { get; set; } = string.Empty;

    [Required]
    public string CarpetaOrigen { get; set; } = string.Empty;

    public DateTime FechaAgregado { get; set; }

    public long DuracionMs { get; set; }

    public string? ThumbnailPath { get; set; }

    public bool Favorito { get; set; }

    public WatchProgress? Progress { get; set; }
}
