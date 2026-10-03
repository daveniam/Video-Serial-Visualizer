// Video Serial Visualizer - Copyright (C) 2026  David Nieves
// SPDX-License-Identifier: GPL-3.0-or-later
// Software libre, sin garantia alguna. Ver LICENSE para los terminos completos.

using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LibVLCSharp.Shared;

// System.Windows.Media tiene su propio MediaPlayer, que choca con el de LibVLC; el alias evita
// tener que calificar el nombre completo en cada uso.
using VlcMediaPlayer = LibVLCSharp.Shared.MediaPlayer;

namespace VideoSerialVisualizer.Helpers;

/// <summary>
/// Hace que LibVLC entregue los cuadros en memoria en vez de dibujarlos el mismo, y los pinta en un
/// <see cref="WriteableBitmap"/> que se puede mostrar con un Image comun de WPF.
///
/// Es el RESPALDO de <see cref="D3DImageVlcRenderer"/>: se usa solo si Direct3D 9Ex no arranca (o
/// con VSV_RENDERER=bitmap). Funciona en cualquier equipo, pero VLC convierte cada cuadro a BGRA
/// por CPU y el hilo de UI copia el cuadro entero en cada fotograma. Medido: 1080p30 sostiene
/// 30 fps sin perder cuadros.
///
/// El tamano lo informa VLC al arrancar cada video (callback de formato); se le pide BGRA ya al
/// tamano de visualizacion (visible y con el aspecto de pixel aplicado, ver
/// <see cref="VlcVideoGeometry"/>), asi la imagen sale lista para mostrar.
/// </summary>
public sealed class VlcFrameRenderer : IVlcFrameRenderer
{
    private readonly Dispatcher _dispatcher;

    /// <summary>Protege el buffer: VLC lo reemplaza al cambiar de video y la UI lo lee al pintar.</summary>
    private readonly object _sync = new();

    // Los delegados DEBEN mantenerse referenciados mientras LibVLC los tenga registrados: si el
    // recolector de basura los libera, el codigo nativo llama a memoria muerta y el proceso se cae
    // sin excepcion atrapable desde .NET.
    private VlcMediaPlayer.LibVLCVideoFormatCb? _formatCb;
    private VlcMediaPlayer.LibVLCVideoCleanupCb? _cleanupCb;
    private VlcMediaPlayer.LibVLCVideoLockCb? _lockCb;
    private VlcMediaPlayer.LibVLCVideoUnlockCb? _unlockCb;
    private VlcMediaPlayer.LibVLCVideoDisplayCb? _displayCb;

    private IntPtr _buffer;
    private int _bufferSize;
    private int _pitch;
    private int _width;
    private int _height;

    private Media? _media;

    /// <summary>1 mientras hay un repintado en vuelo: si VLC entrega mas rapido de lo que la UI
    /// pinta, se descartan cuadros en vez de encolarlos y quedar cada vez mas atrasado.</summary>
    private int _pendingPaint;

    private bool _isDisposed;

    private readonly RendererStatsCounter _stats = new("WriteableBitmap");

    /// <summary>Imagen donde se pinta el video. Se recrea (en el hilo de UI) cuando cambia el tamano;
    /// ver <see cref="FrameChanged"/>.</summary>
    public WriteableBitmap? Frame { get; private set; }

    ImageSource? IVlcFrameRenderer.Frame => Frame;

    public string Name => "WriteableBitmap";

    public event Action<RendererStats>? StatsUpdated;

    public event Action? FrameChanged;

    public VlcFrameRenderer(Dispatcher dispatcher) => _dispatcher = dispatcher;

    public void Attach(VlcMediaPlayer mediaPlayer, Media? media, VideoColorInfo? color)
    {
        if (_isDisposed)
            return;

        lock (_sync)
            _media = media;

        _formatCb = OnVideoFormat;
        _cleanupCb = (ref IntPtr _) => { };

        _lockCb = (_, planes) =>
        {
            Marshal.WriteIntPtr(planes, _buffer);
            return IntPtr.Zero;
        };

        _unlockCb = (_, _, _) => { };

        _displayCb = (_, _) => OnFrameDecoded();

        mediaPlayer.SetVideoFormatCallbacks(_formatCb, _cleanupCb);
        mediaPlayer.SetVideoCallbacks(_lockCb, _unlockCb, _displayCb);
    }

    /// <summary>
    /// Hilo de VLC, al arrancar cada video: se pide BGRA al tamano de visualizacion (VLC convierte y
    /// escala). Si el tamano cambio, se reemplaza el buffer y la UI recrea el bitmap.
    /// </summary>
    private uint OnVideoFormat(ref IntPtr opaque, IntPtr chroma, ref uint width, ref uint height,
        ref uint pitches, ref uint lines)
    {
        try
        {
            var (visibleWidth, visibleHeight, sar) = VlcVideoGeometry.Resolve(_media, (int)width, (int)height);
            var targetWidth = VlcVideoGeometry.DisplayWidth(visibleWidth, sar);
            bool resized;

            lock (_sync)
            {
                if (_isDisposed)
                    return 0;

                resized = targetWidth != _width || visibleHeight != _height || _buffer == IntPtr.Zero;
                if (resized)
                {
                    FreeBuffer();
                    _width = targetWidth;
                    _height = visibleHeight;
                    _pitch = _width * 4;
                    _bufferSize = _pitch * _height;
                    _buffer = Marshal.AllocHGlobal(_bufferSize);
                }

                Interlocked.Exchange(ref _pendingPaint, 0);
            }

            // BGRA es exactamente el formato que consume WriteableBitmap: sin conversion extra.
            Marshal.Copy("BGRA"u8.ToArray(), 0, chroma, 4);
            width = (uint)_width;
            height = (uint)_height;
            pitches = (uint)_pitch;
            lines = (uint)_height;

            if (resized)
            {
                var (w, h) = (_width, _height);
                // BeginInvoke, nunca Invoke: este es el hilo de VLC y la UI podria estar esperandolo
                // (p.ej. dentro de MediaPlayer.Stop), lo que seria un deadlock.
                _dispatcher.BeginInvoke(() =>
                {
                    if (_isDisposed)
                        return;

                    Frame = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgra32, null);
                    FrameChanged?.Invoke();
                });
            }

            return 1;
        }
        catch
        {
            return 0;
        }
    }

    private void OnFrameDecoded()
    {
        if (_isDisposed)
            return;

        if (Interlocked.CompareExchange(ref _pendingPaint, 1, 0) != 0)
        {
            _stats.AddDropped();
            return;
        }

        _dispatcher.BeginInvoke(() =>
        {
            try
            {
                var start = System.Diagnostics.Stopwatch.GetTimestamp();
                lock (_sync)
                {
                    // El bitmap se recrea un instante despues que el buffer: hasta entonces los
                    // tamanos no coinciden y el cuadro se saltea.
                    if (_isDisposed || Frame is null || _buffer == IntPtr.Zero
                        || Frame.PixelWidth != _width || Frame.PixelHeight != _height)
                        return;

                    Frame.WritePixels(new Int32Rect(0, 0, _width, _height), _buffer, _bufferSize, _pitch);
                }

                if (_stats.AddPresented(System.Diagnostics.Stopwatch.GetTimestamp() - start) is { } stats)
                    StatsUpdated?.Invoke(stats);
            }
            catch
            {
                // Un fallo puntual al pintar no debe tumbar la reproduccion: se saltea el cuadro.
            }
            finally
            {
                Interlocked.Exchange(ref _pendingPaint, 0);
            }
        }, DispatcherPriority.Render);
    }

    /// <summary>Desconecta los callbacks (la API nativa acepta NULL como "sin callbacks").</summary>
    public void Detach(VlcMediaPlayer mediaPlayer)
    {
        try
        {
            mediaPlayer.SetVideoCallbacks(null!, null!, null!);
            mediaPlayer.SetVideoFormatCallbacks(null!, null!);
        }
        catch
        {
            // best effort
        }

        lock (_sync)
            _media = null;

        _formatCb = null;
        _cleanupCb = null;
        _lockCb = null;
        _unlockCb = null;
        _displayCb = null;
    }

    /// <summary>Con <see cref="_sync"/> tomado.</summary>
    private void FreeBuffer()
    {
        if (_buffer == IntPtr.Zero)
            return;

        var toFree = _buffer;
        _buffer = IntPtr.Zero;
        Marshal.FreeHGlobal(toFree);
    }

    /// <summary>Debe llamarse con la reproduccion DETENIDA: VLC escribe en el buffer fuera del lock
    /// (entre lock y display), asi que liberarlo con el video andando seria un crash nativo.</summary>
    public void Dispose()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;
        lock (_sync)
        {
            FreeBuffer();
            _media = null;
        }

        _formatCb = null;
        _cleanupCb = null;
        _lockCb = null;
        _unlockCb = null;
        _displayCb = null;
    }
}
