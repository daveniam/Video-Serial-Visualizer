// Video Serial Visualizer - Copyright (C) 2026  David Nieves
// SPDX-License-Identifier: GPL-3.0-or-later
// Software libre, sin garantia alguna. Ver LICENSE para los terminos completos.

using System.Diagnostics;
using System.Runtime.CompilerServices;
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
/// Dos modos:
///   * I420 (el normal): VLC entrega los tres planos Y, U, V tal como salen del decodificador
///     (H.264/H.265 de 8 bits ya vienen asi), sin convertir nada. Se suben como tres texturas de un
///     canal y un pixel shader hace la conversion YUV->RGB en la GPU al dibujar. Sale de la CPU el
///     paso mas caro (con BGRA, VLC convierte cada pixel por software) y la copia es ~2.7x menor
///     (1.5 bytes por pixel en vez de 4).
///   * BGRA (respaldo): si el shader no se puede compilar/crear. VLC convierte por CPU y el cuadro
///     se copia tal cual a la textura.
///
/// En ambos, el trabajo con la GPU lo hace el hilo de video de VLC y se dibuja en la textura "de
/// atras" de un doble buffer; el hilo de UI solo le indica a D3DImage cual mostrar (casi gratis).
/// VLC nunca escribe la textura que WPF esta leyendo.
///
/// Limitacion de VLC 3: entrega los cuadros en memoria de CPU, asi que la subida CPU->GPU sigue
/// existiendo (VLC 4 permitiria dibujar directo en una textura, pero esta en preview).
/// </summary>
public sealed class D3DImageVlcRenderer : IVlcFrameRenderer
{
    private enum PixelMode { I420, Bgra }

    // Destino final (lo que compone WPF). X8R8G8B8 = opaco, sin alfa.
    private const Format TargetFormat = Format.X8R8G8B8;

    /// <summary>
    /// Conversion YUV->RGB. Cada plano llega en una textura L8 (un canal, 0..1). Las constantes
    /// (rango limitado y matriz BT.601/BT.709) se cargan desde C#, ver <see cref="SetColorMatrix"/>.
    /// </summary>
    private const string YuvToRgbShader = """
        sampler2D texY : register(s0);
        sampler2D texU : register(s1);
        sampler2D texV : register(s2);
        float4 offsets : register(c0);
        float4 rowR : register(c1);
        float4 rowG : register(c2);
        float4 rowB : register(c3);

        float4 main(float2 uv : TEXCOORD0) : COLOR0
        {
            float3 yuv = float3(tex2D(texY, uv).r, tex2D(texU, uv).r, tex2D(texV, uv).r) - offsets.xyz;
            return float4(saturate(float3(dot(rowR.xyz, yuv), dot(rowG.xyz, yuv), dot(rowB.xyz, yuv))), 1);
        }
        """;

    private readonly Dispatcher _dispatcher;
    private readonly PixelMode _mode;
    private readonly RendererStatsCounter _stats;

    /// <summary>Protege los recursos D3D: el hilo de VLC los usa en Display y el de UI los libera.</summary>
    private readonly object _sync = new();

    private IDirect3D9Ex? _d3d;
    private IDirect3DDevice9Ex? _device;
    private IDirect3DQuery9? _flushQuery;
    private IDirect3DPixelShader9? _yuvShader;

    // Destino doble buffer (lo que se le da a D3DImage).
    private readonly IDirect3DTexture9?[] _targets = new IDirect3DTexture9?[2];
    private readonly IDirect3DSurface9?[] _targetSurfaces = new IDirect3DSurface9?[2];

    /// <summary>Indice del destino que se dibuja a continuacion; el otro es el que muestra WPF.</summary>
    private int _backIndex;

    // Modo I420: un plano por textura (Y a tamano completo, U y V a la mitad en cada eje).
    private readonly IDirect3DTexture9?[] _planeTextures = new IDirect3DTexture9?[3];

    // Modo BGRA: superficie en memoria de sistema desde la que se copia a la GPU.
    private IDirect3DSurface9? _staging;

    private D3DImage? _image;

    // Delegados referenciados mientras LibVLC los tenga (si el GC los libera, crash nativo).
    private VlcMediaPlayer.LibVLCVideoFormatCb? _formatCb;
    private VlcMediaPlayer.LibVLCVideoCleanupCb? _cleanupCb;
    private VlcMediaPlayer.LibVLCVideoLockCb? _lockCb;
    private VlcMediaPlayer.LibVLCVideoUnlockCb? _unlockCb;
    private VlcMediaPlayer.LibVLCVideoDisplayCb? _displayCb;

    // Memoria donde decodifica VLC. En I420 son tres planos contiguos en el mismo bloque.
    private IntPtr _buffer;
    private readonly IntPtr[] _planes = new IntPtr[3];
    private readonly int[] _pitches = new int[3];
    private readonly int[] _planeWidths = new int[3];
    private readonly int[] _planeHeights = new int[3];
    private int _width;
    private int _height;

    /// <summary>1 mientras la UI no termino de mostrar el ultimo cuadro: si VLC entrega mas rapido,
    /// se descarta en vez de encolar.</summary>
    private int _pendingPaint;

    private bool _isDisposed;

    public string Name { get; private set; }

    public ImageSource? Frame => _image;

    public event Action<RendererStats>? StatsUpdated;

    private D3DImageVlcRenderer(Dispatcher dispatcher, PixelMode mode)
    {
        _dispatcher = dispatcher;
        _mode = mode;
        Name = mode == PixelMode.I420 ? "D3DImage I420" : "D3DImage BGRA";
        _stats = new RendererStatsCounter(Name);
    }

    /// <summary>
    /// Crea el renderer o devuelve null si Direct3D 9Ex no esta disponible (sin GPU, escritorio
    /// remoto viejo, driver roto...); quien lo llama cae al WriteableBitmap. Con
    /// <paramref name="preferI420"/> intenta el modo shader y, si no puede, se queda en BGRA.
    /// </summary>
    public static D3DImageVlcRenderer? TryCreate(Dispatcher dispatcher, bool preferI420 = true)
    {
        if (preferI420)
        {
            var renderer = new D3DImageVlcRenderer(dispatcher, PixelMode.I420);
            try
            {
                renderer.CreateDevice();
                renderer.CreateYuvPipeline();
                return renderer;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[D3DImage] Modo I420 no disponible, se usa BGRA: {ex}");
                renderer.Dispose();
            }
        }

        var fallback = new D3DImageVlcRenderer(dispatcher, PixelMode.Bgra);
        try
        {
            fallback.CreateDevice();
            return fallback;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[D3DImage] No se pudo crear el dispositivo D3D9Ex: {ex}");
            fallback.Dispose();
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

    /// <summary>
    /// Compila el shader y deja fijo todo el estado del dispositivo que no cambia entre cuadros
    /// (D3D9 conserva el estado, asi que se configura una sola vez).
    /// </summary>
    private void CreateYuvPipeline()
    {
        var bytecode = D3DShaderCompiler.Compile(YuvToRgbShader, "main", "ps_2_0");
        _yuvShader = _device!.CreatePixelShader<byte>(bytecode);

        _device.SetRenderState(RenderState.CullMode, (int)Cull.None);
        _device.SetRenderState(RenderState.ZEnable, false);
        _device.SetRenderState(RenderState.Lighting, false);
        _device.SetRenderState(RenderState.AlphaBlendEnable, false);

        for (var i = 0; i < 3; i++)
        {
            // Lineal: U y V tienen la mitad de resolucion y se interpolan al tamano de Y. Clamp
            // para que el borde no "envuelva" y tome color del lado opuesto.
            _device.SetSamplerState(i, SamplerState.MinFilter, (int)TextureFilter.Linear);
            _device.SetSamplerState(i, SamplerState.MagFilter, (int)TextureFilter.Linear);
            _device.SetSamplerState(i, SamplerState.AddressU, (int)TextureAddress.Clamp);
            _device.SetSamplerState(i, SamplerState.AddressV, (int)TextureAddress.Clamp);
        }

        _device.PixelShader = _yuvShader;
        _device.VertexFormat = VertexFormat.PositionRhw | VertexFormat.Texture1;
    }

    /// <summary>Carga en el shader los coeficientes de la conversion (ver <see cref="VideoColorInfo"/>).</summary>
    private void SetColorMatrix(VideoColorInfo color, int height)
    {
        _device!.SetPixelShaderConstant(0, color.ToShaderConstants(height));
    }

    public void Attach(VlcMediaPlayer mediaPlayer, uint width, uint height, VideoColorInfo? color = null)
    {
        if (_isDisposed || _device is null || width == 0 || height == 0)
            return;

        lock (_sync)
        {
            ReleaseFrameResources();

            _width = (int)width;
            _height = (int)height;

            for (var i = 0; i < 2; i++)
            {
                // El handle compartido es lo que permite a WPF (que tiene su PROPIO dispositivo D3D)
                // abrir la textura directo, sin copiarla otra vez.
                var shared = IntPtr.Zero;
                _targets[i] = _device.CreateTexture(width, height, 1, Usage.RenderTarget, TargetFormat,
                    Pool.Default, ref shared);
                _targetSurfaces[i] = _targets[i]!.GetSurfaceLevel(0);
            }

            if (_mode == PixelMode.I420)
                CreateI420Resources(color ?? default);
            else
                CreateBgraResources();

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

            // Se arranca mostrando el destino 1 (vacio) para que el Image ya tenga tamano; el primer
            // cuadro se dibuja en el 0.
            SetBackBuffer(1);
        });

        _lockCb = (_, planes) =>
        {
            // planes es un void*[] de VLC: un puntero por plano (uno solo en BGRA).
            var count = _mode == PixelMode.I420 ? 3 : 1;
            for (var i = 0; i < count; i++)
                Marshal.WriteIntPtr(planes, i * IntPtr.Size, _planes[i]);
            return IntPtr.Zero;
        };

        _unlockCb = (_, _, _) => { };

        _displayCb = (_, _) => OnFrameDecoded();

        if (_mode == PixelMode.I420)
        {
            _formatCb = OnVideoFormat;
            _cleanupCb = (ref IntPtr _) => { };
            mediaPlayer.SetVideoFormatCallbacks(_formatCb, _cleanupCb);
        }
        else
        {
            mediaPlayer.SetVideoFormat("BGRA", width, height, (uint)_pitches[0]);
        }

        mediaPlayer.SetVideoCallbacks(_lockCb, _unlockCb, _displayCb);
    }

    private void CreateI420Resources(VideoColorInfo color)
    {
        var chromaWidth = (_width + 1) / 2;
        var chromaHeight = (_height + 1) / 2;

        _planeWidths[0] = _width;
        _planeHeights[0] = _height;
        _planeWidths[1] = _planeWidths[2] = chromaWidth;
        _planeHeights[1] = _planeHeights[2] = chromaHeight;

        // Pitch alineado a 32 bytes: VLC lo prefiere para sus copias vectorizadas (SIMD).
        long total = 0;
        for (var i = 0; i < 3; i++)
        {
            _pitches[i] = Align(_planeWidths[i], 32);
            total += (long)_pitches[i] * _planeHeights[i];
        }

        _buffer = Marshal.AllocHGlobal((IntPtr)total);
        var offset = 0L;
        for (var i = 0; i < 3; i++)
        {
            _planes[i] = _buffer + (nint)offset;
            offset += (long)_pitches[i] * _planeHeights[i];

            // Dynamic: textura que la CPU reescribe en cada cuadro (LockRect con Discard).
            _planeTextures[i] = _device!.CreateTexture((uint)_planeWidths[i], (uint)_planeHeights[i], 1,
                Usage.Dynamic, Format.L8, Pool.Default);
        }

        SetColorMatrix(color, _height);

        // Se ve en el cartel: que conversion se esta aplicando a ESTE video.
        Name = _stats.Name = $"D3DImage I420 {color.Describe(_height)}";
    }

    private void CreateBgraResources()
    {
        _pitches[0] = _width * 4;
        _planeWidths[0] = _width;
        _planeHeights[0] = _height;
        _buffer = Marshal.AllocHGlobal(_pitches[0] * _height);
        _planes[0] = _buffer;

        _staging = _device!.CreateOffscreenPlainSurface((uint)_width, (uint)_height, TargetFormat, Pool.SystemMemory);
    }

    /// <summary>
    /// Hilo de VLC, al arrancar el video: se le pide I420 al tamano fijado en Attach (si el video es
    /// de otro tamano, VLC lo escala). Devuelve la cantidad de buffers de imagen (1).
    /// </summary>
    private uint OnVideoFormat(ref IntPtr opaque, IntPtr chroma, ref uint width, ref uint height,
        ref uint pitches, ref uint lines)
    {
        // chroma es un char[4] (FourCC) que VLC deja leer y escribir.
        Marshal.Copy("I420"u8.ToArray(), 0, chroma, 4);
        width = (uint)_width;
        height = (uint)_height;

        // pitches y lines son arreglos nativos (uno por plano); LibVLCSharp los expone como ref al
        // primer elemento.
        for (var i = 0; i < 3; i++)
        {
            Unsafe.Add(ref pitches, i) = (uint)_pitches[i];
            Unsafe.Add(ref lines, i) = (uint)_planeHeights[i];
        }

        return 1;
    }

    /// <summary>Hilo de VLC: dibuja el cuadro en el destino de atras y le pide a la UI que lo muestre.</summary>
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
            if (_isDisposed || _device is null || _buffer == IntPtr.Zero || _targetSurfaces[_backIndex] is null)
            {
                Interlocked.Exchange(ref _pendingPaint, 0);
                return;
            }

            filled = _backIndex;
            try
            {
                if (_mode == PixelMode.I420)
                    DrawI420(_targetSurfaces[filled]!);
                else
                    CopyBgra(_targetSurfaces[filled]!);

                // WPF lee la textura desde OTRO dispositivo D3D: hay que garantizar que la GPU
                // termino de escribirla antes de entregarla, o se verian cuadros a medio dibujar.
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
                    if (_targetSurfaces[filled] is null)
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

    /// <summary>Sube los tres planos y dibuja un rectangulo a pantalla completa con el shader.</summary>
    private unsafe void DrawI420(IDirect3DSurface9 target)
    {
        for (var i = 0; i < 3; i++)
        {
            var texture = _planeTextures[i]!;
            var locked = texture.LockRect(0, LockFlags.Discard);
            try
            {
                CopyRows((byte*)_planes[i], _pitches[i], (byte*)locked.DataPointer, locked.Pitch,
                    _planeWidths[i], _planeHeights[i]);
            }
            finally
            {
                texture.UnlockRect(0);
            }
        }

        var device = _device!;
        device.SetRenderTarget(0, target);
        for (var i = 0; i < 3; i++)
            device.SetTexture(i, _planeTextures[i]!);

        // Vertices ya transformados (x, y, z, rhw, u, v). El -0.5 alinea texeles con pixeles: en
        // D3D9 el centro del pixel esta en coordenadas enteras, no en .5.
        float l = -0.5f, t = -0.5f, r = _width - 0.5f, b = _height - 0.5f;
        var vertices = stackalloc float[]
        {
            l, t, 0, 1, 0, 0,
            r, t, 0, 1, 1, 0,
            l, b, 0, 1, 0, 1,
            r, b, 0, 1, 1, 1,
        };

        device.BeginScene();
        try
        {
            device.DrawPrimitiveUP(PrimitiveType.TriangleStrip, 2, (IntPtr)vertices, 6 * sizeof(float));
        }
        finally
        {
            device.EndScene();
        }
    }

    /// <summary>Modo BGRA: memoria de sistema -> superficie de GPU, sin conversion.</summary>
    private unsafe void CopyBgra(IDirect3DSurface9 target)
    {
        var locked = _staging!.LockRect(LockFlags.None);
        try
        {
            CopyRows((byte*)_buffer, _pitches[0], (byte*)locked.DataPointer, locked.Pitch, _width * 4, _height);
        }
        finally
        {
            _staging.UnlockRect();
        }

        _device!.UpdateSurface(_staging, new D3DRect(0, 0, _width, _height), target, new Int2(0, 0));
    }

    /// <summary>Copia fila por fila: el pitch de la textura puede ser mayor que el del buffer
    /// (alineacion del driver). Si coinciden, una sola copia.</summary>
    private static unsafe void CopyRows(byte* src, int srcPitch, byte* dst, int dstPitch, int rowBytes, int rows)
    {
        if (srcPitch == dstPitch)
        {
            var size = (long)srcPitch * rows;
            Buffer.MemoryCopy(src, dst, size, size);
            return;
        }

        for (var y = 0; y < rows; y++)
            Buffer.MemoryCopy(src + (long)y * srcPitch, dst + (long)y * dstPitch, rowBytes, rowBytes);
    }

    /// <summary>Hilo de UI. Muestra el destino indicado.</summary>
    private void SetBackBuffer(int index)
    {
        var surface = _targetSurfaces[index];
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
            // La API nativa acepta NULL como "sin callbacks" (ver VlcFrameRenderer.Detach). Los
            // callbacks de formato hay que limpiarlos aparte: SetVideoFormat (el que usa el
            // WriteableBitmap) NO los pisa, y VLC seguiria pidiendo I420 a un renderer muerto.
            mediaPlayer.SetVideoCallbacks(null!, null!, null!);
            if (_mode == PixelMode.I420)
                mediaPlayer.SetVideoFormatCallbacks(null!, null!);
        }
        catch
        {
            // best effort
        }

        _formatCb = null;
        _cleanupCb = null;
        _lockCb = null;
        _unlockCb = null;
        _displayCb = null;
    }

    private void ReleaseFrameResources()
    {
        for (var i = 0; i < 2; i++)
        {
            _targetSurfaces[i]?.Dispose();
            _targetSurfaces[i] = null;
            _targets[i]?.Dispose();
            _targets[i] = null;
        }

        for (var i = 0; i < 3; i++)
        {
            _planeTextures[i]?.Dispose();
            _planeTextures[i] = null;
            _planes[i] = IntPtr.Zero;
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
            ReleaseFrameResources();
            _yuvShader?.Dispose();
            _yuvShader = null;
            _flushQuery?.Dispose();
            _flushQuery = null;
            _device?.Dispose();
            _device = null;
            _d3d?.Dispose();
            _d3d = null;
        }

        _formatCb = null;
        _cleanupCb = null;
        _lockCb = null;
        _unlockCb = null;
        _displayCb = null;
    }

    private static int Align(int value, int alignment) => (value + alignment - 1) / alignment * alignment;

    [DllImport("user32.dll")]
    private static extern IntPtr GetDesktopWindow();
}
