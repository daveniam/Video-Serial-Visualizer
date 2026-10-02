// Video Serial Visualizer - Copyright (C) 2026  David Nieves
// SPDX-License-Identifier: GPL-3.0-or-later
// Software libre, sin garantia alguna. Ver LICENSE para los terminos completos.

using System.Windows.Media;

// System.Windows.Media tiene su propio MediaPlayer, que choca con el de LibVLC.
using VlcMediaPlayer = LibVLCSharp.Shared.MediaPlayer;

namespace VideoSerialVisualizer.Helpers;

/// <summary>
/// Render del video como contenido WPF (en vez de la ventana nativa de LibVLC). Hay dos
/// implementaciones para poder compararlas: <see cref="VlcFrameRenderer"/> (WriteableBitmap, copia
/// en el hilo de UI) y <see cref="D3DImageVlcRenderer"/> (EXPERIMENTAL: textura Direct3D 9Ex
/// compartida con WPF, la copia se hace fuera del hilo de UI).
/// </summary>
public interface IVlcFrameRenderer : IDisposable
{
    /// <summary>Nombre corto para mostrar en las estadisticas.</summary>
    string Name { get; }

    /// <summary>Imagen donde se pinta el video. Se crea en <see cref="Attach"/>.</summary>
    ImageSource? Frame { get; }

    /// <summary>Se dispara en el hilo de UI aprox. una vez por segundo con las mediciones.</summary>
    event Action<RendererStats>? StatsUpdated;

    /// <summary>Conecta el renderer. Igual que antes: con la reproduccion DETENIDA (LibVLC fija el
    /// destino de video al arrancar) y con el Hwnd ya limpio.</summary>
    void Attach(VlcMediaPlayer mediaPlayer, uint width, uint height);

    /// <summary>Desconecta los callbacks para devolverle el dibujo a LibVLC.</summary>
    void Detach(VlcMediaPlayer mediaPlayer);
}

/// <param name="Presented">Cuadros que llegaron a pantalla en el ultimo segundo.</param>
/// <param name="Dropped">Cuadros que VLC entrego pero se descartaron (la UI todavia no habia
/// pintado el anterior).</param>
/// <param name="UiMsPerFrame">Tiempo promedio que el HILO DE UI gasto por cuadro: es la metrica que
/// esta migracion intenta bajar.</param>
public readonly record struct RendererStats(string Renderer, int Presented, int Dropped, double UiMsPerFrame)
{
    public override string ToString() =>
        $"{Renderer} · {Presented} fps · {Dropped} perdidos · UI {UiMsPerFrame:0.00} ms/cuadro";
}

/// <summary>Acumulador de <see cref="RendererStats"/> compartido por ambos renderers. Solo se toca
/// desde el hilo de UI.</summary>
internal sealed class RendererStatsCounter
{
    private readonly string _name;
    private readonly System.Diagnostics.Stopwatch _window = System.Diagnostics.Stopwatch.StartNew();
    private int _presented;
    private int _dropped;
    private long _uiTicks;

    public RendererStatsCounter(string name) => _name = name;

    public void AddDropped() => Interlocked.Increment(ref _dropped);

    /// <summary>Registra un cuadro pintado; devuelve las estadisticas si se cerro una ventana de 1 s.</summary>
    public RendererStats? AddPresented(long uiTicks)
    {
        _presented++;
        _uiTicks += uiTicks;

        if (_window.ElapsedMilliseconds < 1000)
            return null;

        var seconds = _window.Elapsed.TotalSeconds;
        var stats = new RendererStats(
            _name,
            (int)Math.Round(_presented / seconds),
            (int)Math.Round(Interlocked.Exchange(ref _dropped, 0) / seconds),
            _presented == 0 ? 0 : _uiTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency / _presented);

        _presented = 0;
        _uiTicks = 0;
        _window.Restart();
        return stats;
    }
}
