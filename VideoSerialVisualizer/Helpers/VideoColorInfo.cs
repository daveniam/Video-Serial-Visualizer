// Video Serial Visualizer - Copyright (C) 2026  David Nieves
// SPDX-License-Identifier: GPL-3.0-or-later
// Software libre, sin garantia alguna. Ver LICENSE para los terminos completos.

using System.Numerics;
using FFmpeg.AutoGen;

namespace VideoSerialVisualizer.Helpers;

/// <summary>Matriz con la que se codifico el YUV del video.</summary>
public enum YuvMatrix { Unknown, Bt601, Bt709, Bt2020 }

/// <summary>
/// EXPERIMENTAL (rama experimental/d3dimage). Espacio de color del video, necesario para que el
/// shader de <see cref="D3DImageVlcRenderer"/> convierta YUV->RGB con los coeficientes correctos:
/// con la matriz equivocada los colores salen corridos (rojos anaranjados, verdes apagados).
/// VLC 3 no se lo informa a los callbacks de video, por eso se lee del archivo con FFmpeg.
/// </summary>
public readonly record struct VideoColorInfo(YuvMatrix Matrix, bool FullRange)
{
    /// <summary>
    /// Lee la etiqueta de color de la primera pista de video. Requiere que FFMediaToolkit ya haya
    /// cargado las DLLs de FFmpeg (o sea, despues de abrir el archivo con MediaFile.Open). Devuelve
    /// null si no puede leerlo; nunca lanza.
    /// </summary>
    public static unsafe VideoColorInfo? TryProbe(string path)
    {
        AVFormatContext* context = null;
        try
        {
            if (ffmpeg.avformat_open_input(&context, path, null, null) < 0)
                return null;

            // Necesario para formatos donde la etiqueta vive en el bitstream (VUI de H.264/H.265) y
            // no en el contenedor: FFmpeg la completa al analizar los primeros paquetes.
            if (ffmpeg.avformat_find_stream_info(context, null) < 0)
                return null;

            var index = ffmpeg.av_find_best_stream(context, AVMediaType.AVMEDIA_TYPE_VIDEO, -1, -1, null, 0);
            if (index < 0)
                return null;

            var codecpar = context->streams[index]->codecpar;
            var matrix = codecpar->color_space switch
            {
                AVColorSpace.AVCOL_SPC_BT709 => YuvMatrix.Bt709,
                AVColorSpace.AVCOL_SPC_BT470BG or AVColorSpace.AVCOL_SPC_SMPTE170M => YuvMatrix.Bt601,
                AVColorSpace.AVCOL_SPC_BT2020_NCL or AVColorSpace.AVCOL_SPC_BT2020_CL => YuvMatrix.Bt2020,
                _ => YuvMatrix.Unknown,
            };

            // Los formatos "J" (yuvj420p, tipico de MJPEG y algunas camaras) son rango completo
            // aunque no lo declaren en color_range.
            var pixFmt = (AVPixelFormat)codecpar->format;
            var fullRange = codecpar->color_range == AVColorRange.AVCOL_RANGE_JPEG
                || pixFmt is AVPixelFormat.AV_PIX_FMT_YUVJ420P or AVPixelFormat.AV_PIX_FMT_YUVJ422P
                    or AVPixelFormat.AV_PIX_FMT_YUVJ444P;

            return new VideoColorInfo(matrix, fullRange);
        }
        catch
        {
            return null;
        }
        finally
        {
            if (context is not null)
                ffmpeg.avformat_close_input(&context);
        }
    }

    /// <summary>La matriz que de verdad se aplica: la del archivo, o la convencion si no tiene etiqueta.</summary>
    public YuvMatrix EffectiveMatrix(int videoHeight) =>
        Matrix != YuvMatrix.Unknown ? Matrix : videoHeight >= 720 ? YuvMatrix.Bt709 : YuvMatrix.Bt601;

    /// <summary>Texto corto para las estadisticas, p.ej. "BT.709 lim" o "BT.601? lim" (? = supuesta).</summary>
    public string Describe(int videoHeight)
    {
        var name = EffectiveMatrix(videoHeight) switch
        {
            YuvMatrix.Bt709 => "BT.709",
            YuvMatrix.Bt2020 => "BT.2020",
            _ => "BT.601",
        };
        return $"{name}{(Matrix == YuvMatrix.Unknown ? "?" : "")} {(FullRange ? "full" : "lim")}";
    }

    /// <summary>
    /// Constantes para el shader: [desplazamientos, fila R, fila G, fila B]. Sin etiqueta se usa la
    /// convencion de facto de los reproductores (mpv, navegadores, MPC): BT.709 para HD (720p o
    /// mas), BT.601 para SD.
    /// </summary>
    public Vector4[] ToShaderConstants(int videoHeight)
    {
        var matrix = EffectiveMatrix(videoHeight);

        var (kr, kb) = matrix switch
        {
            YuvMatrix.Bt709 => (0.2126f, 0.0722f),
            YuvMatrix.Bt2020 => (0.2627f, 0.0593f),
            _ => (0.299f, 0.114f),
        };
        var kg = 1 - kr - kb;

        // Rango limitado: Y ocupa 16-235 y U/V 16-240 de los 0-255; hay que estirarlos.
        var yScale = FullRange ? 1f : 255f / 219f;
        var cScale = FullRange ? 1f : 255f / 224f;
        var yOffset = FullRange ? 0f : 16f / 255f;
        const float cOffset = 128f / 255f;

        return
        [
            new Vector4(yOffset, cOffset, cOffset, 0),
            new Vector4(yScale, 0, 2 * (1 - kr) * cScale, 0),
            new Vector4(yScale, -2 * kb * (1 - kb) / kg * cScale, -2 * kr * (1 - kr) / kg * cScale, 0),
            new Vector4(yScale, 2 * (1 - kb) * cScale, 0, 0),
        ];
    }
}
