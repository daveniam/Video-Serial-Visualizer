// Video Serial Visualizer - Copyright (C) 2026  David Nieves
// SPDX-License-Identifier: GPL-3.0-or-later
// Software libre, sin garantia alguna. Ver LICENSE para los terminos completos.

using System.Buffers.Binary;
using System.IO;

namespace VideoSerialVisualizer.Helpers;

/// <summary>
/// Indice de los puntos donde VLC puede saltar limpio dentro de un MP4 FRAGMENTADO (el formato de
/// streaming que queda al descargar un video de una plataforma web: "moof"+"mdat" repetidos).
///
/// El problema: en esos archivos VLC 3 no retrocede al cuadro clave al hacer un seek; empieza a
/// decodificar justo en el punto pedido, sin cuadro de referencia, y se ve gris/con bloques hasta el
/// siguiente cuadro clave (segundos despues). Saltando justo al INICIO de un fragmento (que empieza
/// con cuadro clave) se ve limpio de inmediato. Este indice da esos inicios.
///
/// Se arma leyendo solo las cabeceras de las cajas (un par de KB por fragmento), no el video, asi
/// que es casi instantaneo incluso con archivos de GB. Para un MP4 normal devuelve null y no cambia
/// nada.
/// </summary>
public sealed class FragmentedMp4Index
{
    // Los fragmentos de un MP4 de streaming suelen durar 2-10 s. Si hay saltos mayores (archivos
    // raros con un solo fragmento enorme), ajustar el seek llevaria muy lejos del punto pedido.
    private const long MaxUsefulGapMs = 15_000;

    private readonly long[] _startsMs;

    private FragmentedMp4Index(long[] startsMs) => _startsMs = startsMs;

    /// <summary>Inicios de fragmento (ms, en la linea de tiempo de VLC), ordenados.</summary>
    public IReadOnlyList<long> FragmentStartsMs => _startsMs;

    /// <summary>
    /// Destino de seek que se ve limpio: el inicio de fragmento mas cercano a
    /// <paramref name="targetMs"/> sin pasarse. Si <paramref name="mustExceedMs"/> tiene valor
    /// (un salto HACIA ADELANTE), nunca devuelve algo menor o igual a eso: si el ajuste haria
    /// retroceder, se usa el siguiente inicio.
    /// </summary>
    public long Snap(long targetMs, long? mustExceedMs = null)
    {
        var i = Array.BinarySearch(_startsMs, targetMs);
        if (i < 0)
            i = ~i - 1; // el anterior

        if (i < 0)
            return targetMs; // antes del primer fragmento: no hay nada mejor

        var snapped = _startsMs[i];

        if (mustExceedMs is long floor && snapped <= floor)
        {
            var next = i + 1;
            while (next < _startsMs.Length && _startsMs[next] <= floor)
                next++;
            return next < _startsMs.Length ? _startsMs[next] : targetMs;
        }

        return targetMs - snapped > MaxUsefulGapMs ? targetMs : snapped;
    }

    /// <summary>Arma el indice, o null si el archivo no es un MP4 fragmentado (o no se puede leer).
    /// Nunca lanza.</summary>
    public static FragmentedMp4Index? TryBuild(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.RandomAccess);
            return Build(fs);
        }
        catch
        {
            return null;
        }
    }

    private sealed class TrackInfo
    {
        public uint TrackId;
        public uint Timescale;
        public bool IsVideo;
        public long EditMediaTime; // primer "media_time" del edit list (0 si no hay)
    }

    private static FragmentedMp4Index? Build(FileStream fs)
    {
        var length = fs.Length;
        var head = new byte[16];

        // El archivo tiene que arrancar como un MP4 ("ftyp"); si no, no se toca.
        if (!TryReadBoxHeader(fs, 0, length, head, out var firstType, out _, out _) || firstType != "ftyp")
            return null;

        TrackInfo? video = null;
        var tracks = new List<TrackInfo>();
        var fragmentTimes = new List<long>(); // en unidades del timescale del video
        var moofPositions = new List<(long Start, long End)>();

        long pos = 0;
        while (pos < length)
        {
            if (!TryReadBoxHeader(fs, pos, length, head, out var type, out var headerSize, out var size))
                break;

            if (type == "moov")
                ParseMoov(fs, pos + headerSize, pos + size, tracks);
            else if (type == "moof")
                moofPositions.Add((pos + headerSize, pos + size));

            pos += size;
        }

        if (moofPositions.Count < 2)
            return null; // MP4 normal: VLC busca bien

        video = tracks.FirstOrDefault(t => t.IsVideo && t.Timescale > 0);
        if (video is null)
            return null;

        foreach (var (start, end) in moofPositions)
        {
            if (TryGetFragmentStart(fs, start, end, video.TrackId, out var time))
                fragmentTimes.Add(time);
        }

        if (fragmentTimes.Count < 2)
            return null;

        var starts = fragmentTimes
            .Select(t => (t - video.EditMediaTime) * 1000 / video.Timescale)
            .Where(ms => ms >= 0)
            .Distinct()
            .OrderBy(ms => ms)
            .ToArray();

        return starts.Length >= 2 ? new FragmentedMp4Index(starts) : null;
    }

    // ---- moov: datos de cada pista ----

    private static void ParseMoov(FileStream fs, long start, long end, List<TrackInfo> tracks)
    {
        foreach (var (type, payload, boxEnd) in Children(fs, start, end))
        {
            if (type != "trak")
                continue;

            var track = new TrackInfo();
            ParseTrak(fs, payload, boxEnd, track);
            if (track.TrackId != 0)
                tracks.Add(track);
        }
    }

    private static void ParseTrak(FileStream fs, long start, long end, TrackInfo track)
    {
        foreach (var (type, payload, boxEnd) in Children(fs, start, end))
        {
            switch (type)
            {
                case "tkhd":
                {
                    var b = ReadAt(fs, payload, 24);
                    if (b is null) break;
                    // version 0: 4 (ver/flags) + 4 creation + 4 modification -> track_id
                    // version 1: 4 + 8 + 8 -> track_id
                    track.TrackId = BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(b[0] == 1 ? 20 : 12));
                    break;
                }
                case "edts":
                    foreach (var (t2, p2, _) in Children(fs, payload, boxEnd))
                    {
                        if (t2 != "elst") continue;
                        var b = ReadAt(fs, p2, 28);
                        if (b is null) break;
                        var count = BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(4));
                        if (count == 0) break;
                        // Primera entrada: segment_duration + media_time (32 o 64 bits segun version).
                        // Un media_time de -1 es una "edicion vacia" (retraso), no un recorte.
                        var mediaTime = b[0] == 1
                            ? BinaryPrimitives.ReadInt64BigEndian(b.AsSpan(16))
                            : BinaryPrimitives.ReadInt32BigEndian(b.AsSpan(12));
                        if (mediaTime > 0)
                            track.EditMediaTime = mediaTime;
                    }
                    break;
                case "mdia":
                    foreach (var (t2, p2, _) in Children(fs, payload, boxEnd))
                    {
                        if (t2 == "mdhd")
                        {
                            var b = ReadAt(fs, p2, 24);
                            if (b is null) continue;
                            track.Timescale = BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(b[0] == 1 ? 20 : 12));
                        }
                        else if (t2 == "hdlr")
                        {
                            var b = ReadAt(fs, p2, 12);
                            if (b is null) continue;
                            track.IsVideo = b[8] == (byte)'v' && b[9] == (byte)'i' && b[10] == (byte)'d' && b[11] == (byte)'e';
                        }
                    }
                    break;
            }
        }
    }

    // ---- moof: tiempo de presentacion del primer cuadro de la pista de video ----

    private static bool TryGetFragmentStart(FileStream fs, long start, long end, uint videoTrackId, out long time)
    {
        time = 0;
        foreach (var (type, payload, boxEnd) in Children(fs, start, end))
        {
            if (type != "traf")
                continue;

            uint trackId = 0;
            uint tfhdFlags = 0;
            uint defaultFlags = 0;
            bool hasDefaultFlags = false;
            long? decodeTime = null;
            bool? firstIsSync = null;
            var trunSeen = false;

            foreach (var (t2, p2, e2) in Children(fs, payload, boxEnd))
            {
                if (t2 == "tfhd")
                {
                    var b = ReadAt(fs, p2, (int)Math.Min(e2 - p2, 40));
                    if (b is null || b.Length < 8) return false;
                    tfhdFlags = (uint)(b[1] << 16 | b[2] << 8 | b[3]);
                    trackId = BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(4));
                    var off = 8;
                    if ((tfhdFlags & 0x01) != 0) off += 8; // base_data_offset
                    if ((tfhdFlags & 0x02) != 0) off += 4; // sample_description_index
                    if ((tfhdFlags & 0x08) != 0) off += 4; // default_sample_duration
                    if ((tfhdFlags & 0x10) != 0) off += 4; // default_sample_size
                    if ((tfhdFlags & 0x20) != 0 && off + 4 <= b.Length)
                    {
                        defaultFlags = BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(off));
                        hasDefaultFlags = true;
                    }
                }
                else if (t2 == "tfdt")
                {
                    var b = ReadAt(fs, p2, 12);
                    if (b is null) continue;
                    decodeTime = b[0] == 1
                        ? (long)BinaryPrimitives.ReadUInt64BigEndian(b.AsSpan(4))
                        : BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(4));
                }
                else if (t2 == "trun" && !trunSeen)
                {
                    trunSeen = true; // solo importa el primer trun

                    var b = ReadAt(fs, p2, (int)Math.Min(e2 - p2, 40));
                    if (b is null || b.Length < 8) continue;
                    var flags = (uint)(b[1] << 16 | b[2] << 8 | b[3]);
                    var off = 8; // ver/flags + sample_count
                    if ((flags & 0x001) != 0) off += 4; // data_offset
                    uint? firstFlags = null;
                    if ((flags & 0x004) != 0 && off + 4 <= b.Length)
                    {
                        firstFlags = BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(off));
                        off += 4;
                    }
                    // Primer registro de muestra.
                    if ((flags & 0x100) != 0) off += 4; // duration
                    if ((flags & 0x200) != 0) off += 4; // size
                    if ((flags & 0x400) != 0 && off + 4 <= b.Length)
                        firstFlags ??= BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(off));

                    firstFlags ??= hasDefaultFlags ? defaultFlags : null;
                    if (firstFlags is uint f)
                        firstIsSync = (f & 0x00010000) == 0; // sample_is_non_sync_sample
                }
            }

            if (trackId != videoTrackId)
                continue;

            // Un fragmento que no arranca con cuadro clave no sirve como destino limpio.
            if (decodeTime is null || firstIsSync == false)
                return false;

            // Se usa el tiempo de DECODIFICACION del fragmento (tfdt), no el de presentacion: VLC elige
            // la muestra de arranque comparando el destino contra ese tiempo. Medido con un MP4 de
            // B-frames (presentacion 66 ms despues): pidiendo el tiempo de presentacion ya arranca en
            // la segunda muestra, sin cuadro clave, y se ve gris; pidiendo el de decodificacion, limpio.
            time = decodeTime.Value;
            return true;
        }

        return false;
    }

    // ---- lectura de cajas ----

    private static IEnumerable<(string Type, long Payload, long End)> Children(FileStream fs, long start, long end)
    {
        var head = new byte[16];
        var pos = start;
        while (pos + 8 <= end)
        {
            if (!TryReadBoxHeader(fs, pos, end, head, out var type, out var headerSize, out var size))
                yield break;
            yield return (type, pos + headerSize, pos + size);
            pos += size;
        }
    }

    private static bool TryReadBoxHeader(FileStream fs, long pos, long limit, byte[] head, out string type, out int headerSize, out long size)
    {
        type = "";
        headerSize = 8;
        size = 0;
        if (pos + 8 > limit)
            return false;

        fs.Position = pos;
        if (fs.Read(head, 0, 8) != 8)
            return false;

        size = BinaryPrimitives.ReadUInt32BigEndian(head);
        type = System.Text.Encoding.ASCII.GetString(head, 4, 4);

        if (size == 1)
        {
            if (fs.Read(head, 8, 8) != 8)
                return false;
            size = (long)BinaryPrimitives.ReadUInt64BigEndian(head.AsSpan(8));
            headerSize = 16;
        }
        else if (size == 0)
        {
            size = limit - pos; // hasta el final
        }

        return size >= headerSize && pos + size <= limit;
    }

    private static byte[]? ReadAt(FileStream fs, long pos, int count)
    {
        if (count <= 0)
            return null;
        var buffer = new byte[count];
        fs.Position = pos;
        var read = fs.Read(buffer, 0, count);
        return read == count ? buffer : read > 0 ? buffer[..read] : null;
    }
}
