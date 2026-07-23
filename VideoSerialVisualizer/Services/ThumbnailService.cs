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
    private const int SnapshotTimeoutMs = 6000;
    private const int SeekSettleDelayMs = 400;
    private const int FileWritePollIntervalMs = 100;
    private const int FileWriteTimeoutMs = 2000;

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

    public async Task<string?> GenerateThumbnailAsync(int videoId, string videoPath, long durationMs)
    {
        var outputPath = Path.Combine(ThumbnailDirectory, $"{videoId}.jpg");

        try
        {
            return await CaptureFrameAsync(videoPath, durationMs, outputPath) ? outputPath : null;
        }
        catch
        {
            TryDeleteFile(outputPath);
            return null;
        }
    }

    private async Task<bool> CaptureFrameAsync(string videoPath, long durationMs, string outputPath)
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
                return false;

            if (durationMs > 0)
            {
                mediaPlayer.Time = (long)(durationMs * 0.75);
                await Task.Delay(SeekSettleDelayMs);
            }
            else
            {
                await Task.Delay(SeekSettleDelayMs);
            }

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
        finally
        {
            mediaPlayer.Playing -= onPlaying;
            mediaPlayer.EncounteredError -= onError;
            mediaPlayer.Stop();
        }
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
