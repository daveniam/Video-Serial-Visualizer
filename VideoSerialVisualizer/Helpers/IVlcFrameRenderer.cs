// Video Serial Visualizer - Copyright (C) 2026  David Nieves
// SPDX-License-Identifier: GPL-3.0-or-later
// Software libre, sin garantia alguna. Ver LICENSE para los terminos completos.

using System.Windows.Media;
using LibVLCSharp.Shared;

// System.Windows.Media tiene su propio MediaPlayer, que choca con el de LibVLC.
using VlcMediaPlayer = LibVLCSharp.Shared.MediaPlayer;

namespace VideoSerialVisualizer.Helpers;

/// <summary>
/// Render del video como contenido WPF comun (LibVLC entrega los cuadros por callbacks en vez de
/// dibujar en una ventana nativa). Dos implementaciones: <see cref="D3DImageVlcRenderer"/>
/// (EXPERIMENTAL, la principal: textura Direct3D 9Ex compartida con WPF, conversion de color en la
/// GPU) y <see cref="VlcFrameRenderer"/> (respaldo: WriteableBitmap, todo por CPU).
/// </summary>
public interface IVlcFrameRenderer : IDisposable
{
    /// <summary>Nombre corto para mostrar en las estadisticas.</summary>
    string Name { get; }

    /// <summary>Imagen donde se pinta el video. Puede cambiar de instancia cuando cambia el tamano
    /// del video (ver <see cref="FrameChanged"/>).</summary>
    ImageSource? Frame { get; }

    /// <summary>Se dispara en el hilo de UI cuando <see cref="Frame"/> pasa a ser otra instancia.</summary>
    event Action? FrameChanged;

    /// <summary>Se dispara en el hilo de UI aprox. una vez por segundo con las mediciones.</summary>
    event Action<RendererStats>? StatsUpdated;

    /// <summary>
    /// Conecta el renderer antes de cada Play. El tamano lo informa VLC despues, al arrancar el video
    /// (callback de formato), y los recursos se crean recien ahi.
    /// </summary>
    /// <param name="media">Media que se va a reproducir: de su pista sale el tamano visible y el
    /// aspecto de pixel (VLC ofrece el tamano codificado, ver <see cref="VlcVideoGeometry"/>).</param>
    /// <param name="color">Espacio de color del archivo, si se conoce. Solo lo usa el modo I420 de
    /// D3DImage (la conversion la hace el shader); en los demas la hace VLC.</param>
    void Attach(VlcMediaPlayer mediaPlayer, Media? media, VideoColorInfo? color);

    /// <summary>Desconecta los callbacks para devolverle el dibujo a LibVLC.</summary>
    void Detach(VlcMediaPlayer mediaPlayer);
}

/// <param name="Presented">Cuadros que llegaron a pantalla en el ultimo segundo.</param>
/// <param name="Dropped">Cuadros que VLC entrego pero se descartaron (la UI todavia no habia
/// pintado el anterior).</param>
/// <param name="UiMsPerFrame">Tiempo promedio que el HILO DE UI gasto por cuadro.</param>
/// <param name="CpuPercent">CPU de todo el proceso (100% = todos los nucleos). Incluye la
/// conversion de color que hace VLC por software en BGRA, que es justo lo que el modo I420 evita.</param>
public readonly record struct RendererStats(string Renderer, int Presented, int Dropped, double UiMsPerFrame, double CpuPercent)
{
    public override string ToString() =>
        $"{Renderer} · {Presented} fps · {Dropped} perdidos · UI {UiMsPerFrame:0.00} ms/cuadro · CPU {CpuPercent:0.0}%";
}

/// <summary>Acumulador de <see cref="RendererStats"/> compartido por ambos renderers. Solo se toca
/// desde el hilo de UI.</summary>
internal sealed class RendererStatsCounter
{
    /// <summary>Nombre que aparece en las estadisticas; el renderer lo puede refinar al conectarse.</summary>
    public string Name { get; set; }
    private readonly System.Diagnostics.Stopwatch _window = System.Diagnostics.Stopwatch.StartNew();
    private int _presented;
    private int _dropped;
    private long _uiTicks;
    private readonly System.Diagnostics.Process _process = System.Diagnostics.Process.GetCurrentProcess();
    private TimeSpan _lastCpu = System.Diagnostics.Process.GetCurrentProcess().TotalProcessorTime;

    public RendererStatsCounter(string name) => Name = name;

    public void AddDropped() => Interlocked.Increment(ref _dropped);

    /// <summary>Registra un cuadro pintado; devuelve las estadisticas si se cerro una ventana de 1 s.</summary>
    public RendererStats? AddPresented(long uiTicks)
    {
        _presented++;
        _uiTicks += uiTicks;

        if (_window.ElapsedMilliseconds < 1000)
            return null;

        var seconds = _window.Elapsed.TotalSeconds;

        _process.Refresh();
        var cpu = _process.TotalProcessorTime;
        var cpuPercent = (cpu - _lastCpu).TotalSeconds / seconds / Environment.ProcessorCount * 100;
        _lastCpu = cpu;

        var stats = new RendererStats(
            Name,
            (int)Math.Round(_presented / seconds),
            (int)Math.Round(Interlocked.Exchange(ref _dropped, 0) / seconds),
            _presented == 0 ? 0 : _uiTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency / _presented,
            cpuPercent);

        _presented = 0;
        _uiTicks = 0;
        _window.Restart();
        return stats;
    }
}
