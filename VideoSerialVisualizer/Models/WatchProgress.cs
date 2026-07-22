using System.ComponentModel.DataAnnotations;

namespace VideoSerialVisualizer.Models;

public class WatchProgress
{
    [Key]
    public int Id { get; set; }

    public int VideoId { get; set; }

    public long PosicionMs { get; set; }

    public bool Completado { get; set; }

    public DateTime UltimaVezVisto { get; set; }

    public Video? Video { get; set; }
}
