// Video Serial Visualizer - Copyright (C) 2026  David Nieves
// SPDX-License-Identifier: GPL-3.0-or-later
// Software libre, sin garantia alguna. Ver LICENSE para los terminos completos.

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Vortice.Direct3D9;
using Vortice.Mathematics;

// Vortice.Direct3D9.Rect choca con System.Windows.Rect.
using D3DRect = Vortice.Direct3D9.Rect;

// System.Windows.Media tiene su propio MediaPlayer, que choca con el de LibVLC.
using VlcMediaPlayer = LibVLCSharp.Shared.MediaPlayer;

namespace VideoSerialVisualizer.Helpers;

/// <summary>
/// EXPERIMENTAL (rama experimental/d3dimage). Mismo contrato que <see cref="VlcFrameRenderer"/>,
/// pero el cuadro termina en una textura Direct3D 9Ex que WPF compone directo via
/// <see cref="D3DImage"/>, en vez de en un WriteableBitmap.
///
/// Que cambia respecto del WriteableBitmap: ahi el hilo de UI copia el cuadro entero en cada
/// fotograma (WritePixels, ~8 MB en 1080p, ~33 MB en 4K) y despues WPF lo vuelve a subir a la GPU.
/// Aca la copia CPU->GPU la hace el hilo de video de VLC; el hilo de UI solo intercambia el buffer
/// (Lock / SetBackBuffer / AddDirtyRect / Unlock), que es casi gratis.
///
/// Flujo por cuadro:
///   1. VLC decodifica en <c>_buffer</c> (memoria comun, igual que antes).
///   2. Display (hilo de VLC): se copia a una superficie en memoria de sistema y de ahi
///      (UpdateSurface) a la textura de GPU "de atras"; se espera a que la GPU termine.
///   3. Hilo de UI: D3DImage pasa a mostrar esa textura. La que estaba "adelante" queda libre para
///      el proximo cuadro (doble buffer: VLC nunca escribe la textura que WPF esta leyendo).
///
/// Limitacion de VLC 3: entrega los cuadros en memoria de CPU, asi que la copia CPU->GPU sigue
/// existiendo; solo sale del hilo de UI. El paso siguiente de esta prueba es pedir I420 y convertir
/// YUV->RGB con un shader (hoy esa conversion la hace VLC por CPU al pedirle BGRA).
/// </summary>
public sealed class D3DImageVlcRenderer : IVlcFrameRenderer
{
    // Mismo formato de memoria que el "BGRA" de VLC (B,G,R,X por pixel). X8R8G8B8 (sin alfa) para
    // que WPF lo trate como opaco: el byte de alfa que deja VLC no importa.
    private const Format SurfaceFormat = Format.X8R8G8B8;

    private readonly Dispatcher _dispatcher;
    private readonly RendererStatsCounter _stats = new("D3DImage");

    /// <summary>Protege los recursos D3D: el hilo de VLC los usa en Display y el de UI los libera.</summary>
    private readonly object _sync = new();

    private IDirect3D9Ex? _d3d;
    private IDirect3DDevice9Ex? _device;
    private IDirect3DQuery9? _flushQuery;

    private IDirect3DSurface9? _staging;
    private readonly IDirect3DTexture9?[] _textures = new IDirect3DTexture9?[2];
    private readonly IDirect3DSurface9?[] _surfaces = new IDirect3DSurface9?[2];

    /// <summary>Indice de la textura que VLC llena a continuacion; la otra es la que muestra WPF.</summary>
    private int _backIndex;

    private D3DImage? _image;

    // Delegados referenciados mientras LibVLC los tenga (si el GC los libera, crash nativo).
    private VlcMediaPlayer.LibVLCVideoLockCb? _lockCb;
    private VlcMediaPlayer.LibVLCVideoUnlockCb? _unlockCb;
    private VlcMediaPlayer.LibVLCVideoDisplayCb? _displayCb;

    private IntPtr _buffer;
    private int _pitch;
    private int _width;
    private int _height;

    /// <summary>1 mientras la UI no termino de mostrar el ultimo cuadro: si VLC entrega mas rapido,
    /// se descarta en vez de encolar.</summary>
    private int _pendingPaint;

    private bool _isDisposed;

    public string Name => "D3DImage";

    public ImageSource? Frame => _image;

    public event Action<RendererStats>? StatsUpdated;

    private D3DImageVlcRenderer(Dispatcher dispatcher) => _dispatcher = dispatcher;

    /// <summary>
    /// Crea el renderer o devuelve null si Direct3D 9Ex no esta disponible (sin GPU, escritorio
    /// remoto viejo, driver roto...). Quien lo llama cae al WriteableBitmap.
    /// </summary>
    public static D3DImageVlcRenderer? TryCreate(Dispatcher dispatcher)
    {
        var renderer = new D3DImageVlcRenderer(dispatcher);
        try
        {
            renderer.CreateDevice();
            return renderer;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[D3DImage] No se pudo crear el dispositivo D3D9Ex: {ex}");
            renderer.Dispose();
            return null;
        }
    }

    private void CreateDevice()
    {
        _d3d = D3D9.Direct3DCreate9Ex();

        // El dispositivo nunca presenta en pantalla (solo dibuja a texturas que compone WPF), pero
        // D3D9 exige una ventana igual. Back buffer de 1x1 para no reservar memoria de mas.
        var presentParams = new PresentParameters
        {
            Windowed = true,
            SwapEffect = SwapEffect.Discard,
            DeviceWindowHandle = GetDesktopWindow(),
            PresentationInterval = PresentInterval.Default,
            BackBufferWidth = 1,
            BackBufferHeight = 1,
            BackBufferFormat = Format.Unknown,
        };

        // Multithreaded: lo usan el hilo de VLC (Display) y el de UI (liberar/recrear).
        // FpuPreserve: sin esto D3D9 baja la precision del FPU del proceso y rompe calculos de .NET.
        const CreateFlags baseFlags = CreateFlags.Multithreaded | CreateFlags.FpuPreserve;
        try
        {
            _device = _d3d.CreateDeviceEx(0, DeviceType.Hardware, IntPtr.Zero,
                baseFlags | CreateFlags.HardwareVertexProcessing, presentParams);
        }
        catch
        {
            _device = _d3d.CreateDeviceEx(0, DeviceType.Hardware, IntPtr.Zero,
                baseFlags | CreateFlags.SoftwareVertexProcessing, presentParams);
        }

        _flushQuery = _device.CreateQuery(QueryType.Event);
    }

    public void Attach(VlcMediaPlayer mediaPlayer, uint width, uint height)
    {
        if (_isDisposed || _device is null || width == 0 || height == 0)
            return;

        lock (_sync)
        {
            ReleaseSurfaces();

            _width = (int)width;
            _height = (int)height;
            _pitch = _width * 4;
            _buffer = Marshal.AllocHGlobal(_pitch * _height);

            _staging = _device.CreateOffscreenPlainSurface(width, height, SurfaceFormat, Pool.SystemMemory);

            for (var i = 0; i < 2; i++)
            {
                // El handle compartido es lo que permite a WPF (que tiene su PROPIO dispositivo D3D)
                // abrir la textura directo, sin copiarla otra vez.
                var shared = IntPtr.Zero;
                _textures[i] = _device.CreateTexture(width, height, 1, Usage.RenderTarget, SurfaceFormat,
                    Pool.Default, ref shared);
                _surfaces[i] = _textures[i]!.GetSurfaceLevel(0);
            }

            _backIndex = 0;
            Interlocked.Exchange(ref _pendingPaint, 0);
        }

        // D3DImage tiene afinidad con el hilo de UI.
        _dispatcher.Invoke(() =>
        {
            if (_image is null)
            {
                _image = new D3DImage();
                _image.IsFrontBufferAvailableChanged += OnFrontBufferAvailableChanged;
            }

            // Se arranca mostrando la textura 1 (vacia) para que el Image ya tenga tamano; VLC
            // escribe primero en la 0.
            SetBackBuffer(1);
        });

        _lockCb = (_, planes) =>
        {
            Marshal.WriteIntPtr(planes, _buffer);
            return IntPtr.Zero;
        };

        _unlockCb = (_, _, _) => { };

        _displayCb = (_, _) => OnFrameDecoded();

        mediaPlayer.SetVideoFormat("BGRA", width, height, (uint)_pitch);
        mediaPlayer.SetVideoCallbacks(_lockCb, _unlockCb, _displayCb);
    }

    /// <summary>Hilo de VLC: sube el cuadro a la textura de atras y le pide a la UI que la muestre.</summary>
    private void OnFrameDecoded()
    {
        if (_isDisposed)
            return;

        if (Interlocked.CompareExchange(ref _pendingPaint, 1, 0) != 0)
        {
            _stats.AddDropped();
            return;
        }

        int filled;
        lock (_sync)
        {
            if (_isDisposed || _device is null || _staging is null || _buffer == IntPtr.Zero)
            {
                Interlocked.Exchange(ref _pendingPaint, 0);
                return;
            }

            filled = _backIndex;
            try
            {
                CopyBufferToStaging();
                _device.UpdateSurface(_staging, new D3DRect(0, 0, _width, _height), _surfaces[filled]!, new Int2(0, 0));

                // WPF lee la textura desde OTRO dispositivo D3D: hay que garantizar que la GPU
                // termino de escribirla antes de entregarla, o se verian cuadros a medio copiar.
                _flushQuery!.Issue(Issue.End);
                var done = false;
                while (!_flushQuery.GetData(out done, true) || !done)
                    Thread.SpinWait(50);
            }
            catch (Exception ex)
            {
                // Un cuadro fallido no debe tumbar la reproduccion: se saltea.
                Debug.WriteLine($"[D3DImage] Cuadro descartado: {ex.Message}");
                Interlocked.Exchange(ref _pendingPaint, 0);
                return;
            }
        }

        _dispatcher.BeginInvoke(() =>
        {
            try
            {
                if (_isDisposed || _image is null)
                    return;

                var start = Stopwatch.GetTimestamp();
                lock (_sync)
                {
                    if (_surfaces[filled] is null)
                        return;

                    SetBackBuffer(filled);
                    _backIndex = 1 - filled;
                }

                if (_stats.AddPresented(Stopwatch.GetTimestamp() - start) is { } stats)
                    StatsUpdated?.Invoke(stats);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[D3DImage] Error al presentar: {ex.Message}");
            }
            finally
            {
                Interlocked.Exchange(ref _pendingPaint, 0);
            }
        }, DispatcherPriority.Render);
    }

    private unsafe void CopyBufferToStaging()
    {
        var locked = _staging!.LockRect(LockFlags.None);
        try
        {
            var src = (byte*)_buffer;
            var dst = (byte*)locked.DataPointer;
            var rowBytes = _width * 4;

            // La superficie puede tener un pitch mayor que el ancho (alineacion del driver).
            if (locked.Pitch == _pitch)
            {
                Buffer.MemoryCopy(src, dst, (long)locked.Pitch * _height, (long)_pitch * _height);
            }
            else
            {
                for (var y = 0; y < _height; y++)
                    Buffer.MemoryCopy(src + (long)y * _pitch, dst + (long)y * locked.Pitch, rowBytes, rowBytes);
            }
        }
        finally
        {
            _staging.UnlockRect();
        }
    }

    /// <summary>Hilo de UI. Muestra la textura indicada.</summary>
    private void SetBackBuffer(int index)
    {
        var surface = _surfaces[index];
        if (_image is null || surface is null || !_image.IsFrontBufferAvailable)
            return;

        _image.Lock();
        try
        {
            // enableSoftwareFallback: sigue andando si WPF cae a render por software (escritorio
            // remoto, por ejemplo), a costa de una copia por CPU.
            _image.SetBackBuffer(D3DResourceType.IDirect3DSurface9, surface.NativePointer, true);
            _image.AddDirtyRect(new Int32Rect(0, 0, _image.PixelWidth, _image.PixelHeight));
        }
        finally
        {
            _image.Unlock();
        }
    }

    /// <summary>
    /// WPF suelta el front buffer cuando pierde su dispositivo (bloqueo de pantalla, cambio de
    /// driver, etc.). Al recuperarlo hay que volver a darle la superficie actual.
    /// </summary>
    private void OnFrontBufferAvailableChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_image is null || !_image.IsFrontBufferAvailable)
            return;

        lock (_sync)
            SetBackBuffer(1 - _backIndex);
    }

    public void Detach(VlcMediaPlayer mediaPlayer)
    {
        try
        {
            // La API nativa acepta NULL como "sin callbacks" (ver VlcFrameRenderer.Detach).
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

    private void ReleaseSurfaces()
    {
        for (var i = 0; i < 2; i++)
        {
            _surfaces[i]?.Dispose();
            _surfaces[i] = null;
            _textures[i]?.Dispose();
            _textures[i] = null;
        }

        _staging?.Dispose();
        _staging = null;

        if (_buffer != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_buffer);
            _buffer = IntPtr.Zero;
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;

        // Primero se le quita la superficie a WPF, despues se liberan los recursos.
        if (_image is not null)
        {
            void Clear()
            {
                _image.IsFrontBufferAvailableChanged -= OnFrontBufferAvailableChanged;
                _image.Lock();
                _image.SetBackBuffer(D3DResourceType.IDirect3DSurface9, IntPtr.Zero);
                _image.Unlock();
            }

            if (_dispatcher.CheckAccess())
                Clear();
            else
                _dispatcher.Invoke(Clear);
        }

        lock (_sync)
        {
            ReleaseSurfaces();
            _flushQuery?.Dispose();
            _flushQuery = null;
            _device?.Dispose();
            _device = null;
            _d3d?.Dispose();
            _d3d = null;
        }

        _lockCb = null;
        _unlockCb = null;
        _displayCb = null;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetDesktopWindow();
}
