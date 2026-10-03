// Video Serial Visualizer - Copyright (C) 2026  David Nieves
// SPDX-License-Identifier: GPL-3.0-or-later
// Software libre, sin garantia alguna. Ver LICENSE para los terminos completos.

using LibVLCSharp.Shared;

namespace VideoSerialVisualizer.Helpers;

/// <summary>
/// Tamano real de la imagen de un video para los renderers por callbacks. En el callback de formato
/// LibVLC 3 ofrece el tamano CODIFICADO del buffer (1920x1088 para un 1080p, porque el decodificador
/// trabaja en bloques de 16) y no dice nada del aspecto de pixel; ambas cosas salen de la pista de
/// video de la media.
/// </summary>
internal static class VlcVideoGeometry
{
    /// <summary>
    /// Devuelve el tamano visible (nunca mayor que el buffer) y la relacion de aspecto de pixel
    /// (1 = pixeles cuadrados; distinto de 1 en videos anamorficos, p.ej. DVD). Si la pista no esta
    /// disponible, el buffer entero con pixeles cuadrados.
    /// </summary>
    public static (int Width, int Height, double Sar) Resolve(Media? media, int bufferWidth, int bufferHeight)
    {
        int width = 0, height = 0;
        double sar = 1;

        try
        {
            // Media.Tracks solo toma el lock del item, no el del reproductor: es seguro llamarlo
            // desde el hilo de video, a diferencia de MediaPlayer.Media/Size, que podrian bloquearse
            // contra un Play/Stop en curso en el hilo de UI.
            foreach (var track in media?.Tracks ?? [])
            {
                if (track.TrackType != TrackType.Video)
                    continue;

                width = (int)track.Data.Video.Width;
                height = (int)track.Data.Video.Height;
                if (track.Data.Video.SarNum > 0 && track.Data.Video.SarDen > 0)
                    sar = (double)track.Data.Video.SarNum / track.Data.Video.SarDen;
                break;
            }
        }
        catch
        {
            // sin pista: se usa el buffer
        }

        // Nunca mas grande que el buffer (eso indicaria una pista de otro video o datos raros).
        if (width <= 0 || height <= 0 || width > bufferWidth || height > bufferHeight)
            (width, height) = (bufferWidth, bufferHeight);

        // Aspecto absurdo (metadatos rotos): mejor cuadrado que una imagen aplastada.
        if (sar is < 0.25 or > 4)
            sar = 1;

        return (width, height, sar);
    }

    /// <summary>Ancho de visualizacion: el visible con el aspecto de pixel aplicado.</summary>
    public static int DisplayWidth(int visibleWidth, double sar) => Math.Max(1, (int)Math.Round(visibleWidth * sar));
}
