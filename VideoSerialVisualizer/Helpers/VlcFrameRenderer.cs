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
/// Por que existe: normalmente LibVLC dibuja en una ventana NATIVA de Windows (ver PlayerView.xaml),
/// que queda fuera del compositor de WPF. Eso trae dos consecuencias: no se le puede aplicar la
/// opacidad de la ventana (el video se ve solido aunque todo lo demas se transparente) y nada de WPF
/// puede dibujarse encima. Con este camino el video pasa a ser contenido WPF normal y ambos
/// problemas desaparecen. Medido: 1080p30 sostiene 30 fps sin perder cuadros.
///
/// El costo es una copia del cuadro por fotograma; a cambio se pierde la presentacion acelerada por
/// hardware, asi que se usa solo donde hace falta (la ventana de referencia flotante), no en la
/// reproduccion normal.
/// </summary>
public sealed class VlcFrameRenderer : IDisposable
{
    private readonly Dispatcher _dispatcher;

    // Los delegados DEBEN mantenerse referenciados mientras LibVLC los tenga registrados: si el
    // recolector de basura los libera, el codigo nativo llama a memoria muerta y el proceso se cae
    // sin excepcion atrapable desde .NET.
    private VlcMediaPlayer.LibVLCVideoLockCb? _lockCb;
    private VlcMediaPlayer.LibVLCVideoUnlockCb? _unlockCb;
    private VlcMediaPlayer.LibVLCVideoDisplayCb? _displayCb;

    private IntPtr _buffer;
    private int _bufferSize;
    private int _pitch;
    private Int32Rect _frameRect;

    /// <summary>1 mientras hay un repintado en vuelo: si VLC entrega mas rapido de lo que la UI
    /// pinta, se descartan cuadros en vez de encolarlos y quedar cada vez mas atrasado.</summary>
    private int _pendingPaint;

    private bool _isDisposed;

    /// <summary>Imagen donde se pinta el video. Se crea al configurar el tamano.</summary>
    public WriteableBitmap? Frame { get; private set; }

    /// <summary>Se dispara (en el hilo de UI) la primera vez que hay un cuadro pintado, para que la
    /// vista pueda enlazar la imagen recien entonces.</summary>
    public event Action? FrameReady;

    public VlcFrameRenderer(Dispatcher dispatcher) => _dispatcher = dispatcher;

    /// <summary>
    /// Conecta el renderer a un MediaPlayer. Debe llamarse con la reproduccion DETENIDA: LibVLC fija
    /// el destino de video al arrancar, asi que cambiarlo con el video andando no tiene efecto.
    /// </summary>
    public void Attach(VlcMediaPlayer mediaPlayer, uint width, uint height)
    {
        if (_isDisposed || width == 0 || height == 0)
            return;

        FreeBuffer();

        _pitch = (int)width * 4;
        _bufferSize = _pitch * (int)height;
        _buffer = Marshal.AllocHGlobal(_bufferSize);
        _frameRect = new Int32Rect(0, 0, (int)width, (int)height);

        // El bitmap vive en el hilo de UI (es un objeto de WPF con afinidad de hilo).
        _dispatcher.Invoke(() =>
        {
            Frame = new WriteableBitmap((int)width, (int)height, 96, 96, PixelFormats.Bgra32, null);
        });

        _lockCb = (_, planes) =>
        {
            Marshal.WriteIntPtr(planes, _buffer);
            return IntPtr.Zero;
        };

        _unlockCb = (_, _, _) => { };

        _displayCb = (_, _) =>
        {
            if (_isDisposed)
                return;

            if (Interlocked.CompareExchange(ref _pendingPaint, 1, 0) != 0)
                return;

            _dispatcher.BeginInvoke(() =>
            {
                try
                {
                    if (!_isDisposed && Frame is not null && _buffer != IntPtr.Zero)
                    {
                        Frame.WritePixels(_frameRect, _buffer, _bufferSize, _pitch);
                        FrameReady?.Invoke();
                    }
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
        };

        // BGRA es exactamente el formato que consume WriteableBitmap: sin conversion de color extra.
        mediaPlayer.SetVideoFormat("BGRA", width, height, (uint)_pitch);
        mediaPlayer.SetVideoCallbacks(_lockCb, _unlockCb, _displayCb);
    }

    /// <summary>
    /// Desconecta los callbacks para que LibVLC vuelva a dibujar por su cuenta. Igual que Attach,
    /// requiere la reproduccion detenida.
    /// </summary>
    public void Detach(VlcMediaPlayer mediaPlayer)
    {
        try
        {
            // La firma administrada pide delegados no nulos, pero la API nativa acepta NULL como
            // "sin callbacks": es la unica forma de devolverle el dibujo a LibVLC.
            mediaPlayer.SetVideoCallbacks(null!, null!, null!);
        }
        catch
        {
            // best effort
        }

        _lockCb = null;
        _unlockCb = null;
        _displayCb = null;
    }

    private void FreeBuffer()
    {
        if (_buffer == IntPtr.Zero)
            return;

        var toFree = _buffer;
        _buffer = IntPtr.Zero;
        Marshal.FreeHGlobal(toFree);
    }

    public void Dispose()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;
        _lockCb = null;
        _unlockCb = null;
        _displayCb = null;
        FreeBuffer();
    }
}
