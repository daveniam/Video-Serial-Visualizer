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

    // Posiciones (fraccion de la duracion) donde se intenta capturar, en orden. Si una falla se
    // prueba la siguiente: en algunos videos el punto elegido cae en una transicion o un tramo que
    // el decoder no resuelve bien, pero otro punto si.
    private static readonly double[] SnapshotPositions = { 0.5, 0.35, 0.65, 0.2 };

    private readonly LibVLC _libVlc;
    private readonly IntPtr _hiddenRenderWindow;

    // Nombre historico "TutorialHub" mantenido a proposito: las rutas de las miniaturas quedan
    // guardadas absolutas en la base, renombrar la carpeta romperia las ya generadas.
    // Ver la nota en AppDbContext.DatabaseDirectory.
    public static string ThumbnailDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TutorialHub", "Thumbnails");

    public ThumbnailService(LibVLC libVlc)
    {
        _libVlc = libVlc;
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

    private async Task<(bool Ok, long DurationMs)> CaptureFrameAsync(string videoPath, string outputPath)
    {
        using var media = new Media(_libVlc, new Uri(videoPath));
        using var mediaPlayer = new MediaPlayer(_libVlc) { Volume = 0 };

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
                return (await TrySnapshotAsync(mediaPlayer, outputPath), 0);
            }

            // Se prueban varias posiciones hasta que una produzca un archivo valido.
            foreach (var position in SnapshotPositions)
            {
                mediaPlayer.Time = (long)(durationMs * position);
                await Task.Delay(SeekSettleDelayMs);

                if (await TrySnapshotAsync(mediaPlayer, outputPath))
                    return (true, durationMs);
            }

            return (false, durationMs);
        }
        finally
        {
            mediaPlayer.Playing -= onPlaying;
            mediaPlayer.EncounteredError -= onError;
            mediaPlayer.Stop();
        }
    }

    /// <summary>Toma un snapshot y espera a que el archivo quede escrito con contenido.</summary>
    private static async Task<bool> TrySnapshotAsync(MediaPlayer mediaPlayer, string outputPath)
    {
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
