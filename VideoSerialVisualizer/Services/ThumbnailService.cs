// Video Serial Visualizer - Copyright (C) 2026  David Nieves
// SPDX-License-Identifier: GPL-3.0-or-later
// Software libre, sin garantia alguna. Ver LICENSE para los terminos completos.

using System.IO;
using System.Runtime.InteropServices;
using LibVLCSharp.Shared;

namespace VideoSerialVisualizer.Services;

/// <summary>
/// Genera miniaturas capturando un fotograma real del video con LibVLC.
/// Es "best effort": cualquier fallo (archivo dañado, timeout, códec no soportado)
/// se traga y devuelve null en vez de propagar, para no interrumpir el escaneo de carpetas.
/// </summary>
public class ThumbnailService
{
    // Tiempo maximo esperando a que el video llegue a "Playing". Antes eran 6s: corto para archivos
    // grandes o largos (tutoriales de horas), que tardan mas en abrir y era la causa de que segmentos
    // enteros se quedaran sin miniatura.
    private const int SnapshotTimeoutMs = 12000;
    private const int SeekSettleDelayMs = 500;
    private const int FileWritePollIntervalMs = 100;
    private const int FileWriteTimeoutMs = 2500;

    // Tras un salto, VLC puede mostrar NEGRO un buen rato: si los cuadros clave del video estan muy
    // separados (tipico de grabaciones de diapositivas o de pantalla), no tiene imagen hasta llegar
    // al siguiente. Medido en videos reales: negro durante mas de 2 s despues de saltar. Mientras la
    // captura salga en negro se reintenta en el mismo punto, hasta este limite, antes de pasar al
    // siguiente. En videos normales la primera captura ya sale bien y no se espera nada extra.
    private const int VisibleFrameTimeoutMs = 6000;
    private const int VisibleFrameRetryDelayMs = 500;

    // Posiciones (fraccion de la duracion) donde se intenta capturar, en orden. Si una falla se
    // prueba la siguiente: en algunos videos el punto elegido cae en una transicion o un tramo que
    // el decoder no resuelve bien, pero otro punto si.
    private static readonly double[] SnapshotPositions = { 0.5, 0.35, 0.65, 0.2 };

    // LibVLC se resuelve de forma perezosa: en el arranque se inicializa en segundo plano (fuera del
    // camino critico), asi que la primera miniatura/portada espera a que este listo. Una vez resuelto
    // queda cacheado en _libVlc.
    private readonly Func<Task<LibVLC>> _libVlcProvider;
    private LibVLC? _libVlc;
    private readonly IntPtr _hiddenRenderWindow;

    // Nombre historico "TutorialHub" mantenido a proposito: las rutas de las miniaturas quedan
    // guardadas absolutas en la base, renombrar la carpeta romperia las ya generadas.
    // Ver la nota en AppDbContext.DatabaseDirectory.
    public static string ThumbnailDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TutorialHub", "Thumbnails");

    public ThumbnailService(Func<Task<LibVLC>> libVlcProvider)
    {
        _libVlcProvider = libVlcProvider;
        Directory.CreateDirectory(ThumbnailDirectory);

        // LibVLC necesita una ventana de destino para decodificar/renderizar el video.
        // Si no le damos una, crea la suya propia y parpadea en pantalla mientras
        // generamos miniaturas. Esta ventana nunca se muestra (sin WS_VISIBLE, fuera
        // de pantalla), asi que el proceso queda invisible.
        _hiddenRenderWindow = CreateHiddenWindow();
    }

    /// <summary>Resultado de generar una miniatura: la ruta (o null si fallo) y la duracion real
    /// observada del video. La duracion se lee del reproductor, no de Media.Parse, porque este
    /// ultimo devuelve 0 en varios MP4 (indice al final del archivo), y ese 0 rompia la barra de
    /// progreso y el calculo del punto de captura.</summary>
    public readonly record struct ThumbnailResult(string? ThumbnailPath, long DurationMs);

    public async Task<ThumbnailResult> GenerateThumbnailAsync(int videoId, string videoPath)
    {
        _libVlc ??= await _libVlcProvider();
        var outputPath = Path.Combine(ThumbnailDirectory, $"{videoId}.jpg");

        try
        {
            var (ok, durationMs) = await CaptureFrameAsync(videoPath, outputPath);
            return new ThumbnailResult(ok ? outputPath : null, durationMs);
        }
        catch
        {
            TryDeleteFile(outputPath);
            return new ThumbnailResult(null, 0);
        }
    }

    /// <summary>
    /// Captura un cuadro en un punto PUNTUAL del video (fraccion 0..1) y lo escribe en
    /// <paramref name="outputPath"/>. Es lo que usa la portada elegida a mano: a diferencia de la
    /// miniatura automatica (que prueba varias posiciones fijas), respeta el momento que pidio el
    /// usuario y solo si ese punto exacto no produce imagen prueba unos corrimientos chicos alrededor.
    /// Best effort: devuelve false ante cualquier fallo, sin propagar.
    /// </summary>
    public async Task<bool> CaptureAtPositionAsync(string videoPath, double positionFraction, string outputPath)
    {
        _libVlc ??= await _libVlcProvider();
        try
        {
            return await CaptureAtCoreAsync(videoPath, Math.Clamp(positionFraction, 0, 1), outputPath);
        }
        catch
        {
            TryDeleteFile(outputPath);
            return false;
        }
    }

    private async Task<bool> CaptureAtCoreAsync(string videoPath, double positionFraction, string outputPath)
    {
        using var media = new Media(_libVlc!, new Uri(videoPath));
        using var mediaPlayer = new MediaPlayer(_libVlc!) { Volume = 0 };

        if (_hiddenRenderWindow != IntPtr.Zero)
            mediaPlayer.Hwnd = _hiddenRenderWindow;

        var playingTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var errorTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        EventHandler<EventArgs>? onPlaying = (_, _) => playingTcs.TrySetResult(true);
        EventHandler<EventArgs>? onError = (_, _) => errorTcs.TrySetResult(true);

        mediaPlayer.Playing += onPlaying;
        mediaPlayer.EncounteredError += onError;

        try
        {
            mediaPlayer.Play(media);

            var readyTask = await Task.WhenAny(playingTcs.Task, errorTcs.Task, Task.Delay(SnapshotTimeoutMs));
            if (readyTask != playingTcs.Task)
                return false;

            var durationMs = mediaPlayer.Length;
            var lengthWait = 0;
            while (durationMs <= 0 && lengthWait < 1000)
            {
                await Task.Delay(100);
                durationMs = mediaPlayer.Length;
                lengthWait += 100;
            }

            // Corrimientos (en fraccion de la duracion) alrededor del punto pedido: primero el exacto,
            // luego un poco antes/despues por si ese cuadro cae en una transicion que el decoder no
            // resuelve. Con duracion desconocida no se puede seekear: se captura donde este.
            var offsets = new[] { 0.0, 0.01, -0.01, 0.03, -0.03 };
            foreach (var offset in offsets)
            {
                if (durationMs > 0)
                {
                    var pos = Math.Clamp(positionFraction + offset, 0, 1);
                    mediaPlayer.Time = (long)(durationMs * pos);
                }

                await Task.Delay(SeekSettleDelayMs);

                if (await SnapshotUntilVisibleAsync(mediaPlayer, outputPath))
                    return true;

                if (durationMs <= 0)
                    return HasFile(outputPath);
            }

            // Ningun punto dio una imagen con contenido: se conserva la ultima captura (aunque sea
            // oscura) antes que no dejar nada, como antes.
            return HasFile(outputPath);
        }
        finally
        {
            mediaPlayer.Playing -= onPlaying;
            mediaPlayer.EncounteredError -= onError;
            mediaPlayer.Stop();
        }
    }

    private async Task<(bool Ok, long DurationMs)> CaptureFrameAsync(string videoPath, string outputPath)
    {
        using var media = new Media(_libVlc!, new Uri(videoPath));
        using var mediaPlayer = new MediaPlayer(_libVlc!) { Volume = 0 };

        if (_hiddenRenderWindow != IntPtr.Zero)
            mediaPlayer.Hwnd = _hiddenRenderWindow;

        var playingTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var errorTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        EventHandler<EventArgs>? onPlaying = null;
        EventHandler<EventArgs>? onError = null;
        onPlaying = (_, _) => playingTcs.TrySetResult(true);
        onError = (_, _) => errorTcs.TrySetResult(true);

        mediaPlayer.Playing += onPlaying;
        mediaPlayer.EncounteredError += onError;

        try
        {
            mediaPlayer.Play(media);

            var timeoutTask = Task.Delay(SnapshotTimeoutMs);
            var readyTask = await Task.WhenAny(playingTcs.Task, errorTcs.Task, timeoutTask);

            if (readyTask != playingTcs.Task)
                return (false, 0);

            // La duracion se lee del reproductor ya en marcha: es la fuente confiable. Length puede
            // tardar un instante en poblarse tras el evento Playing, asi que se sondea un momento.
            var durationMs = mediaPlayer.Length;
            var lengthWait = 0;
            while (durationMs <= 0 && lengthWait < 1000)
            {
                await Task.Delay(100);
                durationMs = mediaPlayer.Length;
                lengthWait += 100;
            }
            if (durationMs < 0)
                durationMs = 0;

            // Si aun asi no se conoce la duracion no se puede seekear a una fraccion: se captura
            // donde este (el arranque).
            if (durationMs <= 0)
            {
                await Task.Delay(SeekSettleDelayMs);
                await SnapshotUntilVisibleAsync(mediaPlayer, outputPath);
                return (HasFile(outputPath), 0);
            }

            // Se prueban varias posiciones hasta que una produzca una imagen con contenido. Si todas
            // salen negras (video realmente oscuro), se conserva la ultima captura: el escaneo la
            // vuelve a intentar mas adelante (ver FolderScannerService.HasThumbnail).
            // En un MP4 fragmentado se salta al inicio de un fragmento, que VLC muestra limpio de
            // inmediato (ver FragmentedMp4Index); si no, la imagen sale gris o negra por segundos.
            var seekIndex = Helpers.FragmentedMp4Index.TryBuild(videoPath);

            foreach (var position in SnapshotPositions)
            {
                var target = (long)(durationMs * position);
                mediaPlayer.Time = seekIndex?.Snap(target) ?? target;
                await Task.Delay(SeekSettleDelayMs);

                if (await SnapshotUntilVisibleAsync(mediaPlayer, outputPath))
                    return (true, durationMs);
            }

            return (HasFile(outputPath), durationMs);
        }
        finally
        {
            mediaPlayer.Playing -= onPlaying;
            mediaPlayer.EncounteredError -= onError;
            mediaPlayer.Stop();
        }
    }

    /// <summary>
    /// Captura en la posicion actual hasta que la imagen tenga contenido (no sea negra), o hasta
    /// <see cref="VisibleFrameTimeoutMs"/>. Devuelve true si quedo una imagen con contenido.
    /// </summary>
    private static async Task<bool> SnapshotUntilVisibleAsync(MediaPlayer mediaPlayer, string outputPath)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (true)
        {
            if (await TrySnapshotAsync(mediaPlayer, outputPath) && !IsBlankImage(outputPath))
                return true;

            if (watch.ElapsedMilliseconds + VisibleFrameRetryDelayMs > VisibleFrameTimeoutMs)
                return false;

            await Task.Delay(VisibleFrameRetryDelayMs);
        }
    }

    /// <summary>
    /// La imagen es (casi) negra: lo que deja VLC tras un salto antes de tener cuadro. Se decodifica
    /// a 48 px de ancho, asi que es barato. Si el archivo no se puede leer se considera vacio.
    /// </summary>
    public static bool IsBlankImage(string path)
    {
        try
        {
            var image = new System.Windows.Media.Imaging.BitmapImage();
            image.BeginInit();
            image.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            image.CreateOptions = System.Windows.Media.Imaging.BitmapCreateOptions.IgnoreImageCache;
            image.DecodePixelWidth = 48;
            image.UriSource = new Uri(path);
            image.EndInit();

            var gray = new System.Windows.Media.Imaging.FormatConvertedBitmap(
                image, System.Windows.Media.PixelFormats.Gray8, null, 0);
            var stride = gray.PixelWidth;
            var pixels = new byte[stride * gray.PixelHeight];
            gray.CopyPixels(pixels, stride, 0);

            // Negro "de verdad": todo oscuro y sin detalle claro. Un video oscuro pero con contenido
            // (texto, un personaje iluminado) tiene una parte apreciable de pixeles claros. Se usa
            // el percentil 99 y no el maximo, para que un par de pixeles sueltos (un artefacto del
            // decoder a medio cuadro, el cursor) no hagan pasar por buena una captura negra.
            Array.Sort(pixels);
            var p99 = pixels[(int)(pixels.Length * 0.99)];
            return p99 < 40 && pixels.Average(p => (double)p) < 12;
        }
        catch
        {
            return true;
        }
    }

    private static bool HasFile(string path) => File.Exists(path) && new FileInfo(path).Length > 0;

    /// <summary>Toma un snapshot y espera a que el archivo quede escrito con contenido.</summary>
    private static async Task<bool> TrySnapshotAsync(MediaPlayer mediaPlayer, string outputPath)
    {
        // Se borra la captura anterior: si no, la espera de abajo veria el archivo viejo y daria por
        // escrita una captura que todavia no llego (importa al reintentar en SnapshotUntilVisible).
        TryDeleteFile(outputPath);

        if (!mediaPlayer.TakeSnapshot(0, outputPath, 0, 0))
            return false;

        var waited = 0;
        while (waited < FileWriteTimeoutMs)
        {
            if (File.Exists(outputPath) && new FileInfo(outputPath).Length > 0)
                return true;

            await Task.Delay(FileWritePollIntervalMs);
            waited += FileWritePollIntervalMs;
        }

        return false;
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // best effort
        }
    }

    private static IntPtr CreateHiddenWindow()
    {
        try
        {
            const int WS_POPUP = unchecked((int)0x80000000);

            // Tamano razonable (no 1x1/2x2): un destino de render casi nulo puede hacer que el
            // escalador de video de LibVLC haga calculos inestables con esas dimensiones y
            // desborde la pila nativa (StackOverflowException, no atrapable desde .NET).
            return CreateWindowEx(
                0, "STATIC", "VideoSerialVisualizerThumbnailRenderTarget", WS_POPUP,
                -4000, -4000, 320, 180,
                IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        }
        catch
        {
            return IntPtr.Zero;
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(
        int dwExStyle, string lpClassName, string lpWindowName, int dwStyle,
        int x, int y, int nWidth, int nHeight,
        IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);
}
