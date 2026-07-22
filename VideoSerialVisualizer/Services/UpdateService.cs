using Velopack;
using Velopack.Sources;

namespace VideoSerialVisualizer.Services;

/// <summary>
/// Busca y descarga actualizaciones en segundo plano. La actualizacion NO se aplica mientras el
/// usuario esta usando la app: se deja lista y se instala al cerrarla, asi nunca interrumpe una
/// reproduccion en curso.
/// </summary>
public class UpdateService
{
    /// <summary>
    /// Repositorio de GitHub desde donde se leen las actualizaciones. Velopack busca los archivos
    /// que genera "vpk pack" adjuntos a los Releases del repo, asi que el hosting es gratis.
    ///
    /// Va la URL web del repositorio (https), no la de clonado por SSH.
    /// </summary>
    private const string GithubRepoUrl = "https://github.com/daveniam/Video-Serial-Visualizer";

    /// <summary>Si es true tambien toma releases marcadas como "pre-release" en GitHub.</summary>
    private const bool IncludePrereleases = false;

    private UpdateManager? _manager;
    private UpdateInfo? _downloadedUpdate;

    /// <summary>Version lista para instalarse al cerrar la app, o null si no hay ninguna.</summary>
    public string? PendingVersion => _downloadedUpdate?.TargetFullRelease?.Version?.ToString();

    /// <summary>
    /// Busca actualizaciones y, si hay una, la descarga. Nunca lanza: quedarse sin internet o que
    /// el servidor no responda no debe afectar el uso normal de la app.
    /// </summary>
    public async Task CheckAndDownloadAsync()
    {
        try
        {
            if (GithubRepoUrl.Contains("TODO", StringComparison.OrdinalIgnoreCase))
                return;

            // accessToken null: el repositorio es publico, no hace falta autenticacion.
            var source = new GithubSource(GithubRepoUrl, null, IncludePrereleases);
            var manager = new UpdateManager(source);

            // False cuando se corre desde el build de desarrollo o un copiado suelto de la carpeta:
            // ahi no hay nada que actualizar y aplicar algo romperia la instalacion.
            if (!manager.IsInstalled)
                return;

            var update = await manager.CheckForUpdatesAsync();
            if (update is null)
                return;

            await manager.DownloadUpdatesAsync(update);

            _manager = manager;
            _downloadedUpdate = update;
        }
        catch
        {
            // Sin conexion, servidor caido o feed mal configurado: se reintenta en el proximo arranque.
        }
    }

    /// <summary>
    /// Se llama al cerrar la app: si hay una actualizacion descargada, Velopack la aplica una vez
    /// que este proceso termina. Si no hay ninguna, no hace nada.
    /// </summary>
    public void ApplyPendingUpdateOnExit()
    {
        try
        {
            if (_manager is null || _downloadedUpdate is null)
                return;

            // restart: false -> se instala y no vuelve a abrir la app, porque el usuario la esta cerrando.
            _manager.WaitExitThenApplyUpdates(_downloadedUpdate.TargetFullRelease, false, false);
        }
        catch
        {
            // Si falla, la app simplemente sigue en la version actual.
        }
    }
}
