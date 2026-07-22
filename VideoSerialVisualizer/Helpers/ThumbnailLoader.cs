using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace VideoSerialVisualizer.Helpers;

public static class ThumbnailLoader
{
    /// <summary>
    /// Tope de miniaturas decodificadas que se mantienen en memoria (LRU). Cada una pesa ~500 KB
    /// ya decodificada, asi que sin tope una biblioteca grande consumiria cientos de MB.
    /// </summary>
    private const int MaxCachedThumbnails = 200;

    // Decodificar es CPU-intensivo: sin limite, abrir una vista con cientos de tarjetas lanza
    // cientos de decodificaciones a la vez y satura el thread pool.
    private static readonly SemaphoreSlim DecodeLimiter = new(Math.Max(2, Environment.ProcessorCount / 2));

    private static readonly Dictionary<string, ImageSource> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly LinkedList<string> CacheOrder = new();
    private static readonly object CacheLock = new();

    /// <summary>
    /// Devuelve la miniatura desde la cache o la decodifica en segundo plano. Evita re-decodificar
    /// las mismas imagenes cada vez que se vuelve a una vista, y limita cuantas se procesan a la vez.
    /// </summary>
    public static async Task<ImageSource?> LoadCachedAsync(string? path, int decodePixelWidth = 480)
    {
        if (string.IsNullOrEmpty(path))
            return null;

        if (TryGetCached(path, out var cached))
            return cached;

        await DecodeLimiter.WaitAsync();
        try
        {
            // Otra tarjeta pudo cargar la misma imagen mientras esperabamos el turno.
            if (TryGetCached(path, out cached))
                return cached;

            var image = await Task.Run(() => TryLoad(path, decodePixelWidth));
            if (image is not null)
                AddToCache(path, image);

            return image;
        }
        finally
        {
            DecodeLimiter.Release();
        }
    }

    private static bool TryGetCached(string path, out ImageSource? image)
    {
        lock (CacheLock)
        {
            if (Cache.TryGetValue(path, out var found))
            {
                // Marcarla como la usada mas recientemente.
                CacheOrder.Remove(path);
                CacheOrder.AddFirst(path);
                image = found;
                return true;
            }
        }

        image = null;
        return false;
    }

    private static void AddToCache(string path, ImageSource image)
    {
        lock (CacheLock)
        {
            if (!Cache.ContainsKey(path))
                CacheOrder.AddFirst(path);

            Cache[path] = image;

            while (CacheOrder.Count > MaxCachedThumbnails)
            {
                var oldest = CacheOrder.Last;
                if (oldest is null)
                    break;

                CacheOrder.RemoveLast();
                Cache.Remove(oldest.Value);
            }
        }
    }

    /// <summary>
    /// Decodifica la imagen de forma que pueda llamarse desde un hilo de fondo
    /// (Task.Run) sin bloquear la UI. El BitmapImage resultante se congela
    /// para poder pasarlo de forma segura al hilo de UI.
    ///
    /// <paramref name="decodePixelWidth"/> limita el tamano al que se decodifica: las tarjetas
    /// miden ~240px, asi que decodificar a resolucion completa desperdicia memoria y CPU. Un ancho
    /// acotado reduce mucho el consumo y hace mas fluido el scroll y el redibujado al escalar.
    /// </summary>
    public static ImageSource? TryLoad(string? path, int decodePixelWidth = 480)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return null;

        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            if (decodePixelWidth > 0)
                bitmap.DecodePixelWidth = decodePixelWidth;
            bitmap.UriSource = new Uri(path, UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }
}
