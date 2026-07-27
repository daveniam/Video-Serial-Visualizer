// Video Serial Visualizer - Copyright (C) 2026  David Nieves
// SPDX-License-Identifier: GPL-3.0-or-later
// Software libre, sin garantia alguna. Ver LICENSE para los terminos completos.

using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FFMediaToolkit;
using FFMediaToolkit.Decoding;
using FFMediaToolkit.Graphics;
using LibVLCSharp.Shared;
using VideoSerialVisualizer.Helpers;
using VideoSerialVisualizer.Localization;
using VideoSerialVisualizer.Models;
using VideoSerialVisualizer.Services;
using VideoSerialVisualizer.Views;

namespace VideoSerialVisualizer.ViewModels;

public partial class PlayerViewModel : ObservableObject, IDisposable
{
    private readonly LibVLC _libVlc;
    private readonly ProgressTrackerService _progressTracker;
    private readonly VideoMarkerService _markerService;
    private readonly Action _goBack;
    private readonly DispatcherTimer _saveTimer;
    private readonly DispatcherTimer _opacitySaveTimer;

    private Media? _media;
    private bool _hasAppliedSavedPosition;
    private bool _playbackStarted;
    private bool _isSeekingFromUser;
    private long _lastScrubSeekTick;
    private bool _hasLoadedSubtitleTracks;
    private bool _isSyncingSubtitleSelection;
    private IntPtr _videoHwnd;

    /// <summary>Ventana (en ms) antes del final durante la cual se rellena el boton "Siguiente".</summary>
    private const double NextFillWindowMs = 30_000;

    /// <summary>
    /// Margen (ms) contra el final: una posicion guardada dentro de este margen se considera "el
    /// video ya termino" y se reinicia desde cero en vez de reanudar.
    /// </summary>
    private const long ResumeEndGuardMs = 1_000;

    /// <summary>
    /// Duracion minima (ms) de un segmento marcado. Evita loops absurdamente cortos entre un IN y
    /// un OUT casi pegados, que rebotarian sin parar.
    /// </summary>
    private const double MinSegmentLengthMs = 300;

    private IReadOnlyList<Video> _playlist = Array.Empty<Video>();
    private bool _isAdvancing;
    private bool _skipPositionSaveOnce;

    // --- Asentamiento de seeks contra VLC ---
    //
    // LibVLC no aplica MediaPlayer.Time al instante: mientras procesa el seek internamente (buscar
    // keyframe, reencuadrar el decoder), puede seguir emitiendo TimeChanged con la posicion ANTERIOR
    // durante un rato. Si en ese momento ya no se esta suprimiendo la actualizacion de PositionMs
    // (p.ej. porque el usuario ya solto el mouse), esos eventos rezagados pisan la posicion nueva
    // con la vieja: se ve como si el slider "rebotara" elasticamente unas cuantas veces antes de
    // asentarse en el lugar correcto. Se resuelve ignorando los TimeChanged que reporten una
    // posicion lejos del destino recien pedido, hasta que converjan o venza un margen de seguridad.

    /// <summary>Margen (ms): un TimeChanged se acepta como "ya reflejando el seek" cuando NO esta
    /// mas de esto por detras del destino. Chico a proposito: si fuera grande, se enancharia la
    /// perilla al keyframe anterior (unos cientos de ms atras) y se veria el rebote. Con un margen
    /// chico, cualquier reporte por detras se ignora y la perilla queda fija donde se la dejo.</summary>
    private const double SeekSettleToleranceMs = 60;

    /// <summary>Tope de tiempo por si el seek nunca cae lo bastante cerca del valor pedido (poco
    /// frecuente, pero posible segun el codec/los keyframes): pasado esto se deja de ignorar
    /// TimeChanged aunque no haya convergido, para no trabar el seguimiento de posicion.</summary>
    private static readonly TimeSpan SeekSettleTimeout = TimeSpan.FromSeconds(2);

    private double? _pendingSeekTargetMs;
    private DateTime _pendingSeekDeadlineUtc;

    // --- Paso a cuadro exacto (modo animador) ---
    //
    // LibVLC solo sabe avanzar un cuadro exacto (NextFrame), no retroceder. Para tener paso a
    // cuadro EXACTO en ambos sentidos se usa un decodificador FFmpeg aparte (FFMediaToolkit): al
    // entrar en este modo, VLC se pausa y el area de video (ventana nativa, ver PlayerView.xaml)
    // se reemplaza por una imagen WPF con el cuadro que decodifica FFmpeg. Se sale del modo
    // reanudando la reproduccion (Play/clic), momento en que VLC retoma desde la misma posicion.
    private static bool _ffmpegPathConfigured;
    private MediaFile? _frameStepFile;
    private double _frameStepFps;
    private int? _frameStepTotalFrames;
    private int _frameStepIndex;
    private bool _isFrameStepBusy;

    // Abrir el archivo con FFmpeg (EnsureFrameStepFileOpen) se dispara desde DOS lugares que no se
    // coordinan entre si: el paso a cuadro interactivo (hilo de UI) y el sondeo automatico al abrir
    // un video en modo animador (Task.Run en segundo plano, ver LoadVideoAsync). El lock evita que
    // ambos abran el archivo en paralelo; la generacion evita que un sondeo lento, que termina
    // DESPUES de que el usuario ya paso a otro video, pise el estado del video actual.
    private readonly object _frameStepFileLock = new();
    private int _frameStepGeneration;

    /// <summary>Render del video como imagen WPF (modo animador). Null cuando dibuja LibVLC.</summary>
    private VlcFrameRenderer? _frameRenderer;

    private int _videoPixelWidth;
    private int _videoPixelHeight;

    /// <summary>
    /// Relacion de aspecto real del video (ancho/alto). La usa la ventana de referencia para
    /// mantener su forma al redimensionar, y asi enmarcar el video exacto sin franjas negras.
    /// Null mientras no se conozca todavia el tamano del archivo.
    /// </summary>
    public double? VideoAspectRatio =>
        _videoPixelWidth > 0 && _videoPixelHeight > 0 ? (double)_videoPixelWidth / _videoPixelHeight : null;

    /// <summary>
    /// Cuantos cuadros extra se decodifican de mas, en sentido de avance, cada vez que un retroceso
    /// obliga a un seek real. El seek en si (buscar el keyframe anterior) es lo caro y domina el
    /// costo total (medido: bajar este numero a la mitad solo recorta el "miss" en una fraccion
    /// chica); una vez ahi, seguir decodificando hacia adelante es casi gratis. Asi, retroceder
    /// varias veces seguidas dentro de esta ventana no vuelve a pagar el seek.
    /// </summary>
    private const int FrameStepBackfillCount = 15;

    /// <summary>
    /// Tope del cache de cuadros decodificados. Cada cuadro cacheado son sus pixeles crudos sin
    /// comprimir (p.ej. ~8MB en 1080p BGRA32), asi que este numero acota la memoria a proposito
    /// (60 cuadros rondan los 250-500MB segun resolucion) en vez de dejar crecer el cache sin limite.
    /// </summary>
    /// <summary>
    /// Presupuesto de memoria del cache de cuadros. Se limita por BYTES y no por cantidad de
    /// cuadros a proposito: un cuadro pesa distinto segun la resolucion (~8 MB en 1080p pero ~33 MB
    /// en 4K), asi que un tope fijo de N cuadros que es razonable en 1080p se vuelve varios GB en
    /// 4K y puede tumbar la aplicacion por falta de memoria. Con un tope en bytes, el cache guarda
    /// menos cuadros cuanto mayor sea la resolucion, y el consumo queda acotado siempre.
    /// </summary>
    private const long FrameStepCacheBudgetBytes = 300L * 1024 * 1024;

    /// <summary>Bytes ocupados ahora mismo por los cuadros cacheados.</summary>
    private long _frameStepCacheBytes;

    private readonly Dictionary<int, RawFrame> _frameStepCache = new();
    private readonly Queue<int> _frameStepCacheOrder = new();

    /// <summary>
    /// Copia de los pixeles de un cuadro decodificado, SIN convertir a BitmapSource todavia. Se
    /// cachea en este formato (barato) en vez de como BitmapSource (caro: ver ToFrozenBitmapSource)
    /// para no pagar la conversion de los ~25 cuadros de un lote de backfill cuando solo hace falta
    /// mostrar uno.
    /// </summary>
    private readonly record struct RawFrame(byte[] Pixels, int Width, int Height, int Stride);

    public MediaPlayer MediaPlayer { get; }

    [ObservableProperty]
    private Video? currentVideo;

    [ObservableProperty]
    private bool isPlaying;

    [ObservableProperty]
    private double positionMs;

    [ObservableProperty]
    private double durationMs;

    public double ProgressPercent => DurationMs > 0 ? Math.Clamp(PositionMs / DurationMs * 100.0, 0, 100) : 0;

    [ObservableProperty]
    private string currentTimeText = "00:00";

    [ObservableProperty]
    private string totalTimeText = "00:00";

    [ObservableProperty]
    private int volume = 100;

    private int _volumeBeforeMute = 100;

    public bool IsMuted => Volume <= 0;

    public ObservableCollection<SubtitleTrackOption> SubtitleTracks { get; } = new();

    /// <summary>Etiquetas marcadas en la linea de tiempo del video actual (modo animador).</summary>
    public ObservableCollection<VideoMarkerViewModel> Markers { get; } = new();

    [ObservableProperty]
    private bool hasSubtitleTracks;

    [ObservableProperty]
    private bool hasMultipleSubtitleTracks;

    [ObservableProperty]
    private bool subtitlesEnabled;

    [ObservableProperty]
    private SubtitleTrackOption? selectedSubtitleTrack;

    /// <summary>Hay un video despues del actual en la lista de reproduccion.</summary>
    [ObservableProperty]
    private bool hasNextVideo;

    /// <summary>Hay un video antes del actual en la lista de reproduccion.</summary>
    [ObservableProperty]
    private bool hasPreviousVideo;

    /// <summary>Velocidades de reproduccion disponibles (fijas, no cambian en tiempo de ejecucion).</summary>
    public IReadOnlyList<PlaybackSpeedOption> PlaybackSpeeds => PlaybackSpeedOption.All;

    /// <summary>
    /// Velocidad elegida. Se mantiene entre videos: si estabas viendo a 1.5x, el siguiente arranca
    /// igual, que es lo esperable cuando se recorre un curso entero.
    /// </summary>
    [ObservableProperty]
    private PlaybackSpeedOption selectedPlaybackSpeed = PlaybackSpeedOption.Normal;

    /// <summary>El boton "Siguiente" se muestra recien en los ultimos segundos del video.</summary>
    [ObservableProperty]
    private bool isNextFillVisible;

    /// <summary>0-100: cuanto se lleno el boton "Siguiente" dentro de la ventana de 30s final.</summary>
    [ObservableProperty]
    private double nextFillPercent;

    /// <summary>
    /// Modo animador activo (preferencia del usuario). Muestra las herramientas de estudio cuadro a
    /// cuadro en el reproductor. Se relee de <see cref="AppSettings"/> al abrir cada video: la
    /// configuracion solo se abre desde Explorar (con el reproductor cerrado), asi que aplicar el
    /// cambio en la proxima reproduccion alcanza y evita tener que observar el archivo en vivo.
    /// </summary>
    [ObservableProperty]
    private bool isAnimatorModeEnabled;

    /// <summary>Piso de opacidad: por debajo de esto la ventana se vuelve tan invisible que cuesta
    /// encontrarla para devolverla a la normalidad.</summary>
    public const double MinWindowOpacityPercent = 20;

    /// <summary>
    /// Opacidad de TODA la ventana (20-100), no solo del video: se aplica a nivel Win32 para que
    /// alcance tambien a la ventana nativa donde dibuja LibVLC (ver WindowEffectsHelper).
    /// </summary>
    [ObservableProperty]
    private double windowOpacityPercent = 100;

    /// <summary>Evita que restaurar la preferencia al abrir un video dispare un guardado redundante.</summary>
    private bool _isRestoringWindowOpacity;

    /// <summary>Porcentaje de opacidad como texto ("75%"), para mostrarlo junto al slider.</summary>
    public string WindowOpacityText => $"{Math.Round(WindowOpacityPercent)}%";

    /// <summary>Opacidad como fraccion 0-1, que es lo que consume Window.Opacity de la ventana de referencia.</summary>
    public double WindowOpacityFraction => Math.Clamp(WindowOpacityPercent, MinWindowOpacityPercent, 100) / 100.0;

    /// <summary>La ventana de referencia se mantiene encima de las demas aplicaciones.</summary>
    [ObservableProperty]
    private bool isReferenceAlwaysOnTop = true;

    [RelayCommand]
    private void ToggleReferenceAlwaysOnTop() => IsReferenceAlwaysOnTop = !IsReferenceAlwaysOnTop;

    /// <summary>
    /// Los clics atraviesan la ventana de referencia y llegan a la aplicacion de abajo: permite
    /// dibujar "a traves" de la referencia. La ventana en si aplica el efecto y maneja el atajo
    /// global para salir (ver ReferenceWindow); aca solo vive el estado para poder enlazarlo.
    /// </summary>
    [ObservableProperty]
    private bool isReferenceClickThrough;

    [RelayCommand]
    private void ToggleReferenceClickThrough()
    {
        // Un click-through que quedo debajo de otra ventana no sirve de nada: al activarlo se fuerza
        // que quede encima.
        if (!IsReferenceClickThrough)
            IsReferenceAlwaysOnTop = true;

        IsReferenceClickThrough = !IsReferenceClickThrough;
    }

    partial void OnWindowOpacityPercentChanged(double value)
    {
        OnPropertyChanged(nameof(WindowOpacityText));
        OnPropertyChanged(nameof(WindowOpacityFraction));

        if (_isRestoringWindowOpacity)
            return;

        // Arrastrar el slider dispara decenas de cambios por segundo; se agrupa para no reescribir
        // el archivo de preferencias en cada uno.
        _opacitySaveTimer.Stop();
        _opacitySaveTimer.Start();
    }

    /// <summary>
    /// Reproduccion en bucle. Sin segmento marcado, repite el video completo; con segmento marcado
    /// (<see cref="SegmentStartMs"/>/<see cref="SegmentEndMs"/>), repite solo esa franja.
    /// </summary>
    [ObservableProperty]
    private bool isLoopEnabled;

    /// <summary>Punto de entrada del segmento marcado (null = sin marcar).</summary>
    [ObservableProperty]
    private double? segmentStartMs;

    /// <summary>Punto de salida del segmento marcado (null = sin marcar).</summary>
    [ObservableProperty]
    private double? segmentEndMs;

    public bool HasSegment => SegmentStartMs.HasValue && SegmentEndMs.HasValue;

    /// <summary>Posicion del punto de entrada como porcentaje del video, para dibujar la banda del segmento.</summary>
    public double SegmentStartPercent =>
        DurationMs > 0 && SegmentStartMs.HasValue ? Math.Clamp(SegmentStartMs.Value / DurationMs * 100.0, 0, 100) : 0;

    /// <summary>Ancho de la banda del segmento como porcentaje del video.</summary>
    public double SegmentBandWidthPercent =>
        DurationMs > 0 && HasSegment ? Math.Clamp((SegmentEndMs!.Value - SegmentStartMs!.Value) / DurationMs * 100.0, 0, 100) : 0;

    /// <summary>Tramo posterior al punto de salida como porcentaje del video (completa las 3 columnas de la banda).</summary>
    public double SegmentAfterPercent =>
        DurationMs > 0 && SegmentEndMs.HasValue ? Math.Clamp(100 - SegmentEndMs.Value / DurationMs * 100.0, 0, 100) : 0;

    /// <summary>Texto "mm:ss – mm:ss" del segmento marcado, vacio si no hay segmento.</summary>
    public string SegmentRangeText => HasSegment ? $"{FormatTime(SegmentStartMs!.Value)} – {FormatTime(SegmentEndMs!.Value)}" : string.Empty;

    /// <summary>
    /// Activo mientras se muestra un cuadro exacto decodificado por FFmpeg en vez del video en vivo
    /// de VLC. La vista oculta la superficie nativa y muestra <see cref="FrameStepImage"/> en su lugar.
    /// </summary>
    [ObservableProperty]
    private bool isFrameStepActive;

    /// <summary>Cuadro actual decodificado por FFmpeg, visible solo mientras <see cref="IsFrameStepActive"/>.</summary>
    [ObservableProperty]
    private BitmapSource? frameStepImage;

    /// <summary>Numero de cuadro actual (0-based), null hasta que se entra al modo paso a cuadro.</summary>
    [ObservableProperty]
    private int? currentFrameNumber;

    /// <summary>
    /// Cuadro en vivo cuando el video se dibuja como imagen WPF (modo animador) en vez de en la
    /// ventana nativa de LibVLC. Ver <see cref="VlcFrameRenderer"/> para el por que.
    /// </summary>
    [ObservableProperty]
    private WriteableBitmap? videoFrame;

    /// <summary>El video se esta dibujando por callbacks (imagen WPF) y no en la ventana nativa.</summary>
    [ObservableProperty]
    private bool isCallbackRenderingActive;

    // Con la ventana de referencia abierta, la ventana principal deja de dibujar el video: las dos
    // muestran el MISMO bitmap (VLC decodifica y se copia una sola vez), pero WPF lo compondria dos
    // veces. Y sobre todo: mientras se usa la referencia flotando sobre otro programa, nadie esta
    // mirando el video de la ventana principal, asi que ese trabajo es puro desperdicio.

    /// <summary>La ventana nativa de LibVLC es la que muestra el video ahora mismo.</summary>
    public bool IsNativeVideoVisible => !IsFrameStepActive && !IsCallbackRenderingActive && !IsReferenceWindowOpen;

    /// <summary>La imagen WPF del render por callbacks es la que muestra el video ahora mismo.</summary>
    public bool IsCallbackVideoVisible => !IsFrameStepActive && IsCallbackRenderingActive && !IsReferenceWindowOpen;

    /// <summary>El cuadro congelado del paso a cuadro se muestra en la ventana principal.</summary>
    public bool IsFrameStepVisibleInMain => IsFrameStepActive && !IsReferenceWindowOpen;

    /// <summary>La ventana principal esta activa. Cuando no lo esta, el overlay de play se oculta para
    /// que no quede flotando por encima de otras aplicaciones (el Popup vive en su propia ventana).</summary>
    [ObservableProperty]
    private bool isWindowActive = true;

    /// <summary>
    /// Boton de play grande al centro del video (estilo YouTube), visible solo con el video pausado.
    /// Se excluye el paso a cuadro (ya tiene su cuadro congelado) y la ventana de referencia (el
    /// video se muestra alla).
    /// </summary>
    public bool IsPlayOverlayVisible =>
        CurrentVideo is not null && _playbackStarted && !IsPlaying
        && !IsFrameStepActive && !IsReferenceWindowOpen && IsWindowActive;

    private void NotifyVideoSurfaceVisibilityChanged()
    {
        OnPropertyChanged(nameof(IsNativeVideoVisible));
        OnPropertyChanged(nameof(IsCallbackVideoVisible));
        OnPropertyChanged(nameof(IsFrameStepVisibleInMain));
        OnPropertyChanged(nameof(IsPlayOverlayVisible));
    }

    partial void OnIsCallbackRenderingActiveChanged(bool value) => NotifyVideoSurfaceVisibilityChanged();

    partial void OnIsFrameStepActiveChanged(bool value) => NotifyVideoSurfaceVisibilityChanged();

    partial void OnIsReferenceWindowOpenChanged(bool value) => NotifyVideoSurfaceVisibilityChanged();

    partial void OnIsPlayingChanged(bool value) => OnPropertyChanged(nameof(IsPlayOverlayVisible));

    partial void OnCurrentVideoChanged(Video? value) => OnPropertyChanged(nameof(IsPlayOverlayVisible));

    partial void OnIsWindowActiveChanged(bool value) => OnPropertyChanged(nameof(IsPlayOverlayVisible));

    /// <summary>Texto "Cuadro N" localizado; vacio si no hay numero de cuadro conocido todavia.</summary>
    public string FrameNumberText => CurrentFrameNumber.HasValue
        ? string.Format(Loc.I["Player_FrameNumberFormat"], CurrentFrameNumber.Value)
        : string.Empty;

    partial void OnCurrentFrameNumberChanged(int? value) => OnPropertyChanged(nameof(FrameNumberText));

    /// <summary>Tope de marcas de cuadro dibujadas en la barra: mas alla de esto, en un video largo,
    /// cada marca dejaria de representar un unico cuadro para no ocupar menos de 1px cada una.</summary>
    private const int MaxVisibleFrameTicks = 300;

    /// <summary>Cantidad total de cuadros del video, conocida una vez que se sondea con FFmpeg
    /// (ver EnsureFrameStepFileOpen). Null hasta entonces o si el modo animador esta apagado.</summary>
    public int? TotalFrameCount => _frameStepTotalFrames;

    /// <summary>
    /// Cuantas marcas de cuadro se dibujan realmente en la barra. En un video corto (hasta
    /// <see cref="MaxVisibleFrameTicks"/> cuadros) es 1 marca = 1 cuadro real; en uno largo se topea
    /// para que las marcas sigan siendo distinguibles (cada una pasa a representar varios cuadros).
    /// </summary>
    public int VisibleFrameTickCount => Math.Min(TotalFrameCount ?? 0, MaxVisibleFrameTicks);

    /// <summary>Coleccion dummy de tamano <see cref="VisibleFrameTickCount"/>: el ItemsControl de la
    /// vista solo necesita la CANTIDAD de elementos, no su contenido, para dibujar las marcas.</summary>
    public IEnumerable<int> FrameTickIndexes => Enumerable.Range(0, Math.Max(0, VisibleFrameTickCount));

    /// <summary>Hay al menos 2 marcas para dibujar (con 0 o 1 no tiene sentido mostrar divisiones).</summary>
    public bool HasFrameTicks => VisibleFrameTickCount > 1;

    /// <summary>Alto del contenedor de la barra de progreso: mas alto en modo animador para que la
    /// division en cuadros sea evidente (pedido explicito del usuario).</summary>
    public double SeekBarContainerHeight => IsAnimatorModeEnabled ? 26 : 14;

    /// <summary>Alto de la franja interna (reproducido/pendiente, banda de segmento, marcas de cuadro).</summary>
    public double SeekBarTrackHeight => IsAnimatorModeEnabled ? 14 : 4;

    /// <summary>Alto fijo del marcador de etiqueta (rombo) dibujado sobre la barra.</summary>
    private const double MarkerVisualHeight = 14;

    /// <summary>Canvas.Top para centrar el marcador dentro de la barra, que cambia de alto segun
    /// el modo animador (ver SeekBarContainerHeight).</summary>
    public double MarkerVerticalOffset => (SeekBarContainerHeight - MarkerVisualHeight) / 2;

    partial void OnIsAnimatorModeEnabledChanged(bool value)
    {
        OnPropertyChanged(nameof(SeekBarContainerHeight));
        OnPropertyChanged(nameof(SeekBarTrackHeight));
        OnPropertyChanged(nameof(MarkerVerticalOffset));
    }

    /// <summary>Refresca los bindings derivados del sondeo de FFmpeg (cantidad de cuadros, marcas).
    /// Se llama a mano porque estos valores no vienen de un [ObservableProperty]: se calculan a
    /// partir de campos privados que EnsureFrameStepFileOpen puede completar en segundo plano.</summary>
    private void NotifyFrameTickPropertiesChanged()
    {
        OnPropertyChanged(nameof(TotalFrameCount));
        OnPropertyChanged(nameof(VisibleFrameTickCount));
        OnPropertyChanged(nameof(FrameTickIndexes));
        OnPropertyChanged(nameof(HasFrameTicks));
        OnPropertyChanged(nameof(SegmentStartTickIndex));
        OnPropertyChanged(nameof(SegmentEndTickIndex));
        OnPropertyChanged(nameof(VideoAspectRatio));
    }

    /// <summary>Indice (dentro de FrameTickIndexes) de la marca que corresponde al punto de entrada
    /// del segmento; null si no hay entrada marcada o si la cantidad de cuadros aun no se conoce.</summary>
    public int? SegmentStartTickIndex => ToTickIndex(SegmentStartMs);

    /// <summary>Indice (dentro de FrameTickIndexes) de la marca que corresponde al punto de salida
    /// del segmento; null si no hay salida marcada o si la cantidad de cuadros aun no se conoce.</summary>
    public int? SegmentEndTickIndex => ToTickIndex(SegmentEndMs);

    private int? ToTickIndex(double? positionMs)
    {
        if (!positionMs.HasValue || DurationMs <= 0 || VisibleFrameTickCount <= 0)
            return null;

        var fraction = Math.Clamp(positionMs.Value / DurationMs, 0, 1);
        return Math.Min(VisibleFrameTickCount - 1, (int)(fraction * VisibleFrameTickCount));
    }

    public PlayerViewModel(LibVLC libVlc, ProgressTrackerService progressTracker, VideoMarkerService markerService, Action goBack)
    {
        _libVlc = libVlc;
        _progressTracker = progressTracker;
        _markerService = markerService;
        _goBack = goBack;

        MediaPlayer = new MediaPlayer(_libVlc);

        // La app maneja el input (clic para pausar/reanudar); si LibVLC lo procesa por su cuenta
        // se come los clics de su ventana de video y ademas responde a teclas por su lado.
        MediaPlayer.EnableMouseInput = false;
        MediaPlayer.EnableKeyInput = false;

        MediaPlayer.Playing += OnPlaying;
        MediaPlayer.Paused += OnPausedOrStopped;
        MediaPlayer.Stopped += OnPausedOrStopped;
        MediaPlayer.EndReached += OnEndReached;
        MediaPlayer.TimeChanged += OnTimeChanged;
        MediaPlayer.EncounteredError += OnEncounteredError;

        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        _saveTimer.Tick += async (_, _) => await SaveCurrentPositionAsync();

        _opacitySaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        _opacitySaveTimer.Tick += (_, _) =>
        {
            _opacitySaveTimer.Stop();
            var settings = AppSettings.Load();
            settings.AnimatorWindowOpacityPercent = WindowOpacityPercent;
            settings.Save();
        };
    }

    /// <summary>
    /// La vista registra su ventana nativa (Hwnd) al cargarse; LibVLC renderiza el video dentro de
    /// ella en vez de crear su propia ventana flotante. Debe llamarse ANTES de reproducir.
    /// </summary>
    public void AttachVideoSurface(IntPtr hwnd)
    {
        _videoHwnd = hwnd;
        MediaPlayer.Hwnd = hwnd;
    }

    public void DetachVideoSurface()
    {
        _videoHwnd = IntPtr.Zero;
        MediaPlayer.Hwnd = IntPtr.Zero;
    }

    public async Task LoadVideoAsync(Video video, IReadOnlyList<Video>? playlist = null)
    {
        // Al encadenar con el siguiente video el actual ya termino: su progreso se guardo como
        // completado y MediaPlayer.Time ya no es confiable, asi que no se re-guarda la posicion.
        if (_skipPositionSaveOnce)
            _skipPositionSaveOnce = false;
        else
            await SaveCurrentPositionAsync();

        if (playlist is not null)
            _playlist = playlist;

        // Se relee la preferencia en cada apertura para que un cambio en Configuracion tome efecto
        // sin reiniciar (aplica al siguiente video que se abre).
        var settings = AppSettings.Load();
        IsAnimatorModeEnabled = settings.AnimatorModeEnabled;

        // Con el modo animador apagado la ventana vuelve a ser opaca si o si: la opacidad es una
        // herramienta de ese modo, no debe quedar aplicada para quien lo desactivo.
        _isRestoringWindowOpacity = true;
        WindowOpacityPercent = IsAnimatorModeEnabled
            ? Math.Clamp(settings.AnimatorWindowOpacityPercent, MinWindowOpacityPercent, 100)
            : 100;
        _isRestoringWindowOpacity = false;

        CurrentVideo = video;

        // El overlay de play no debe verse durante la carga (aun no arranco): se habilita recien
        // con el primer evento Playing (ver OnPlaying). Evita un parpadeo al abrir cada video.
        _playbackStarted = false;
        OnPropertyChanged(nameof(IsPlayOverlayVisible));

        HasNextVideo = GetNextVideo() is not null;
        HasPreviousVideo = GetPreviousVideo() is not null;
        IsNextFillVisible = false;
        NextFillPercent = 0;

        // El loop y el segmento marcado son propios de la linea de tiempo de ESTE video; no tiene
        // sentido que un segmento marcado en un video aparezca en el siguiente.
        IsLoopEnabled = false;
        SegmentStartMs = null;
        SegmentEndMs = null;

        // El handle de FFmpeg del paso a cuadro esta atado al archivo anterior; se descarta y se
        // vuelve a abrir (perezosamente al pasar a cuadro, o de entrada si el modo animador esta
        // activo, ver mas abajo) si el nuevo video lo necesita.
        IsFrameStepActive = false;
        FrameStepImage = null;
        CurrentFrameNumber = null;
        _frameStepFile?.Dispose();
        _frameStepFile = null;
        _frameStepFps = 0;
        _frameStepTotalFrames = null;
        _videoPixelWidth = 0;
        _videoPixelHeight = 0;
        _frameStepCache.Clear();
        _frameStepCacheOrder.Clear();
        _frameStepCacheBytes = 0;
        _frameStepGeneration++;
        NotifyFrameTickPropertiesChanged();
        NotifyFrameStepAvailabilityChanged();

        // Un seek pendiente del video anterior no tiene sentido para el nuevo (su propia secuencia
        // de TimeChanged arranca de cero).
        _pendingSeekTargetMs = null;

        _hasAppliedSavedPosition = false;
        _hasLoadedSubtitleTracks = false;
        SubtitleTracks.Clear();
        HasSubtitleTracks = false;
        HasMultipleSubtitleTracks = false;
        SubtitlesEnabled = false;
        SelectedSubtitleTrack = null;

        // Las etiquetas de linea de tiempo son propias del modo animador, igual que el resto de
        // estas herramientas: no tiene sentido pagar la consulta a la base para quien no lo usa.
        Markers.Clear();
        if (IsAnimatorModeEnabled)
        {
            var markers = await _markerService.GetMarkersAsync(video.Id);
            foreach (var marker in markers)
                Markers.Add(new VideoMarkerViewModel(marker, video.DuracionMs));
        }

        var progress = await _progressTracker.GetProgressAsync(video.Id);
        var savedPosition = progress?.PosicionMs ?? 0;

        // Un video ya terminado se reabre desde el principio. Si se reanudara en el final,
        // EndReached se dispararia de inmediato y encadenaria al siguiente, dejandolo imposible
        // de volver a ver (pasa al reabrirlo desde la biblioteca o con "video anterior").
        var alreadyFinished = progress?.Completado == true
            || (video.DuracionMs > 0 && savedPosition >= video.DuracionMs - ResumeEndGuardMs);

        PositionMs = alreadyFinished ? 0 : savedPosition;
        DurationMs = video.DuracionMs;
        CurrentTimeText = FormatTime(PositionMs);
        TotalTimeText = FormatTime(video.DuracionMs);

        _media?.Dispose();
        _media = new Media(_libVlc, new Uri(video.RutaAbsoluta));

        if (IsAnimatorModeEnabled)
        {
            // En modo animador el sondeo con FFmpeg se hace ANTES de reproducir y se espera: hace
            // falta el tamano del video para configurar el render por callbacks. De paso quedan
            // listos fps y cantidad de cuadros desde el primer instante, asi las marcas de la barra
            // y los botones de paso a cuadro no aparecen con retraso.
            await Task.Run(EnsureFrameStepFileOpen);
            NotifyFrameTickPropertiesChanged();
            NotifyFrameStepAvailabilityChanged();

            AttachFrameRenderer();
        }
        else
        {
            DetachFrameRenderer();
        }

        MediaPlayer.Play(_media);
        MediaPlayer.Volume = Volume;
        _saveTimer.Start();
    }

    /// <summary>
    /// Pasa el video al render por callbacks (imagen WPF). Si el sondeo con FFmpeg no pudo abrir el
    /// archivo, se queda con la ventana nativa: es preferible perder la transparencia que no poder
    /// ver el video.
    /// </summary>
    private void AttachFrameRenderer()
    {
        var size = _frameStepFile?.Video.Info.FrameSize;
        if (size is not { Width: > 0, Height: > 0 })
        {
            DetachFrameRenderer();
            return;
        }

        // El ORDEN importa: en LibVLC "dibujar en una ventana" y "entregar los cuadros por callbacks"
        // son excluyentes y gana lo ultimo que se configura. Si se fijara el Hwnd DESPUES de los
        // callbacks, VLC los descartaria y volveria a modo ventana; como el handle es nulo, se
        // crearia su propia ventana flotante aparte (sintoma: el video en vivo aparece en una
        // ventana suelta y la imagen WPF nunca se actualiza). Por eso el Hwnd se limpia primero.
        MediaPlayer.Hwnd = IntPtr.Zero;

        _frameRenderer ??= new VlcFrameRenderer(App.Current.Dispatcher);
        _frameRenderer.Attach(MediaPlayer, (uint)size.Value.Width, (uint)size.Value.Height);

        VideoFrame = _frameRenderer.Frame;
        IsCallbackRenderingActive = true;
    }

    /// <summary>Devuelve el dibujo del video a la ventana nativa de LibVLC (camino rapido normal).</summary>
    private void DetachFrameRenderer()
    {
        if (_frameRenderer is not null)
        {
            _frameRenderer.Detach(MediaPlayer);
            _frameRenderer.Dispose();
            _frameRenderer = null;
        }

        VideoFrame = null;
        IsCallbackRenderingActive = false;

        // Reafirmar el Hwnd justo antes de reproducir: si LibVLC no tiene una ventana de destino
        // al hacer Play, crea la suya propia (video flotante en una ventana aparte).
        if (_videoHwnd != IntPtr.Zero)
            MediaPlayer.Hwnd = _videoHwnd;
    }

    // Los handlers de MediaPlayer se disparan en el hilo interno de LibVLC. Se usa BeginInvoke
    // (no bloqueante) en vez de Invoke: si se bloqueara esperando a la UI y la UI a su vez
    // estuviera esperando a que ese mismo hilo termine (p.ej. MediaPlayer.Stop()), la app se
    // congela (deadlock cruzado entre el hilo de LibVLC y el hilo de UI).

    private void OnPlaying(object? sender, EventArgs e)
    {
        App.Current.Dispatcher.BeginInvoke(() =>
        {
            IsPlaying = true;
            _playbackStarted = true;

            if (!_hasAppliedSavedPosition)
            {
                _hasAppliedSavedPosition = true;
                if (PositionMs > 0)
                {
                    MediaPlayer.Time = (long)PositionMs;
                    BeginSeekSettleWindow(PositionMs);
                }
            }

            if (!_hasLoadedSubtitleTracks)
            {
                _hasLoadedSubtitleTracks = true;
                RefreshSubtitleTracks();
            }

            // LibVLC vuelve a 1x con cada media nuevo, asi que la velocidad elegida se reaplica
            // cada vez que arranca una reproduccion, no solo cuando el usuario la cambia.
            ApplyPlaybackSpeed();
        });
    }

    private void OnPausedOrStopped(object? sender, EventArgs e)
    {
        App.Current.Dispatcher.BeginInvoke(() => IsPlaying = false);
    }

    private void OnEndReached(object? sender, EventArgs e)
    {
        App.Current.Dispatcher.BeginInvoke(async () =>
        {
            // Red de seguridad del loop: en el camino normal OnTimeChanged ya salta de vuelta antes
            // de llegar al final real (ver TryGetLoopBoundary), pero si LibVLC reporta EndReached
            // antes de un ultimo TimeChanged, se retoma el loop en vez de tratar esto como "el video
            // termino" (evitando el encadenado automatico al siguiente).
            if (IsLoopEnabled && TryGetLoopBoundary(out var loopStart, out _))
            {
                SeekToLoopStart(loopStart);
                MediaPlayer.Play();
                return;
            }

            IsPlaying = false;
            if (CurrentVideo is not null)
                await _progressTracker.SaveProgressAsync(CurrentVideo.Id, DurationMs > 0 ? (long)DurationMs : (long)PositionMs, (long)DurationMs);

            // Encadenar con el siguiente. Se hace desde el hilo de UI (BeginInvoke), nunca desde el
            // hilo de eventos de LibVLC: llamar Play() ahi puede colgar el reproductor.
            await AdvanceToNextAsync();
        });
    }

    private void OnEncounteredError(object? sender, EventArgs e)
    {
        App.Current.Dispatcher.BeginInvoke(() =>
        {
            _saveTimer.Stop();
            IsPlaying = false;

            var nombre = CurrentVideo?.NombreArchivo ?? "este video";
            MessageBox.Show(
                string.Format(Loc.I["Error_Playback_Message"], nombre),
                Loc.I["Error_Playback_Title"],
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            _goBack();
        });
    }

    private void OnTimeChanged(object? sender, MediaPlayerTimeChangedEventArgs e)
    {
        App.Current.Dispatcher.BeginInvoke(() =>
        {
            if (_isSeekingFromUser)
                return;

            // Tras un seek, VLC salta al keyframe ANTERIOR al punto pedido, asi que reporta por un
            // instante una posicion detras de donde el usuario solto la perilla. Se mantiene la
            // perilla FIJA en el destino y se ignoran esos reportes rezagados; se vuelve a seguir la
            // posicion recien cuando la reproduccion realmente alcanza o pasa el destino (o vence el
            // margen de seguridad). En pausa no llegan mas TimeChanged, asi que la perilla queda
            // exactamente donde se la dejo, sin rebote.
            if (_pendingSeekTargetMs is double seekTarget)
            {
                if (e.Time < seekTarget - SeekSettleToleranceMs && DateTime.UtcNow < _pendingSeekDeadlineUtc)
                    return;

                _pendingSeekTargetMs = null;
            }

            // Se corta ANTES de actualizar PositionMs: en cuanto la reproduccion alcanza o pasa el
            // punto de salida del loop, se salta de vuelta al inicio sin llegar a mostrar ese ultimo
            // instante fuera de rango.
            if (IsLoopEnabled && TryGetLoopBoundary(out var loopStart, out var loopEnd) && e.Time >= loopEnd)
            {
                SeekToLoopStart(loopStart);
                return;
            }

            PositionMs = e.Time;
            CurrentTimeText = FormatTime(e.Time);
            UpdateNextFill();
        });
    }

    /// <summary>
    /// Registra que se acaba de pedir un seek a VLC hacia <paramref name="targetMs"/>: hasta que
    /// converja (ver OnTimeChanged), los TimeChanged rezagados de ANTES de este seek se ignoran en
    /// vez de pisar la posicion nueva con la vieja (el "rebote elastico" del slider).
    /// </summary>
    private void BeginSeekSettleWindow(double targetMs)
    {
        _pendingSeekTargetMs = targetMs;
        _pendingSeekDeadlineUtc = DateTime.UtcNow + SeekSettleTimeout;
    }

    /// <summary>
    /// Limites del loop activo: el segmento marcado si hay uno, o el video completo si no.
    /// Devuelve false si el rango es invalido (video sin duracion conocida todavia, o un segmento
    /// mas corto que <see cref="MinSegmentLengthMs"/>) para que el llamador no intente loopear.
    /// </summary>
    private bool TryGetLoopBoundary(out double start, out double end)
    {
        start = SegmentStartMs ?? 0;
        end = SegmentEndMs ?? DurationMs;
        return end - start >= MinSegmentLengthMs;
    }

    private void SeekToLoopStart(double start)
    {
        MediaPlayer.Time = (long)start;
        BeginSeekSettleWindow(start);
        PositionMs = start;
        CurrentTimeText = FormatTime(start);
    }

    // FFmpegLoader.FFmpegPath es un estado global de la libreria (no por instancia); se fija una
    // sola vez, recien cuando hace falta, para no imponerle este costo/riesgo a quien nunca usa el
    // modo animador. Las DLLs nativas viven junto al ejecutable (ver VideoSerialVisualizer.csproj).
    private static void EnsureFFmpegPathConfigured()
    {
        if (_ffmpegPathConfigured)
            return;

        FFmpegLoader.FFmpegPath = Path.Combine(AppContext.BaseDirectory, "ffmpeg");
        _ffmpegPathConfigured = true;
    }

    /// <summary>
    /// Abre (si no esta abierto ya) el decodificador FFmpeg del video actual, en formato BGRA32
    /// para poder envolver los bytes decodificados directo en un BitmapSource de WPF sin conversion.
    /// Silencioso ante errores (DLL nativa ausente, archivo no soportado): el llamador trata
    /// "_frameStepFile sigue null" como "el paso a cuadro no esta disponible ahora".
    ///
    /// Se llama desde DOS lugares sin coordinar entre si (paso a cuadro interactivo en el hilo de
    /// UI, y el sondeo automatico en segundo plano al abrir un video en modo animador), por eso el
    /// lock: sin el, ambos podrian terminar abriendo el mismo archivo en paralelo. La generacion
    /// descarta el resultado si, mientras se abria, el usuario ya paso a otro video.
    /// </summary>
    private void EnsureFrameStepFileOpen()
    {
        if (_frameStepFile is not null || CurrentVideo is null)
            return;

        lock (_frameStepFileLock)
        {
            // Doble chequeo: otra llamada pudo haber terminado de abrirlo mientras se esperaba el lock.
            if (_frameStepFile is not null || CurrentVideo is null)
                return;

            var video = CurrentVideo;
            var generation = _frameStepGeneration;

            try
            {
                EnsureFFmpegPathConfigured();

                var options = new MediaOptions
                {
                    StreamsToLoad = MediaMode.Video,
                    VideoPixelFormat = ImagePixelFormat.Bgra32,
                };

                var file = MediaFile.Open(video.RutaAbsoluta, options);
                var fps = file.Video.Info.AvgFrameRate;
                var numberOfFrames = file.Video.Info.NumberOfFrames;
                var totalFrames = numberOfFrames.HasValue && numberOfFrames.Value > 0
                    ? (int)numberOfFrames.Value
                    : (fps > 0 && video.DuracionMs > 0 ? (int?)Math.Round(video.DuracionMs / 1000.0 * fps) : null);

                if (generation != _frameStepGeneration)
                {
                    // El usuario ya paso a otro video mientras esto se abria: se descarta.
                    file.Dispose();
                    return;
                }

                _frameStepFile = file;
                _frameStepFps = fps;
                _frameStepTotalFrames = totalFrames;

                // Se guardan aparte (y no se leen de _frameStepFile cuando hacen falta) porque la
                // ventana de referencia los consulta desde el hilo de UI mientras el decodificador
                // esta usando ese mismo objeto.
                _videoPixelWidth = file.Video.Info.FrameSize.Width;
                _videoPixelHeight = file.Video.Info.FrameSize.Height;
            }
            catch
            {
                _frameStepFile = null;
                _frameStepFps = 0;
                _frameStepTotalFrames = null;
            }
        }
    }

    /// <summary>
    /// Avanza o retrocede exactamente <paramref name="delta"/> cuadros usando FFmpeg (exacto en
    /// ambos sentidos, a diferencia de VLC que solo sabe avanzar). La primera vez que se llama en
    /// un video pausa VLC y calcula el cuadro actual a partir de la posicion de reproduccion.
    ///
    /// No abre el archivo por su cuenta: <see cref="CanStepFrame"/> ya garantiza que el sondeo
    /// automatico (ver LoadVideoAsync) lo dejo listo antes de que este metodo sea alcanzable.
    /// </summary>
    private async Task StepFrameAsync(int delta)
    {
        if (_isFrameStepBusy || _frameStepFile is null || _frameStepFps <= 0)
            return;

        _isFrameStepBusy = true;
        try
        {
            if (!IsFrameStepActive)
            {
                MediaPlayer.SetPause(true);
                _frameStepIndex = (int)Math.Round(PositionMs / 1000.0 * _frameStepFps);
            }

            var targetIndex = Math.Max(0, _frameStepIndex + delta);

            if (!_frameStepCache.TryGetValue(targetIndex, out var rawFrame))
            {
                // Cache miss: hace falta un seek real. Al retroceder, en vez de decodificar SOLO el
                // cuadro pedido, se arranca un poco antes (FrameStepBackfillCount) y se decodifica
                // secuencialmente hasta el destino: el seek (caro) se paga una vez, y el resto del
                // lote queda cacheado (bytes crudos, ver nota abajo) para los proximos retrocesos
                // dentro de esa ventana.
                var batchStart = delta < 0 ? Math.Max(0, targetIndex - FrameStepBackfillCount) : targetIndex;

                var file = _frameStepFile;
                var fps = _frameStepFps;
                var batch = await Task.Run(() => DecodeFrameBatch(file, fps, batchStart, targetIndex));

                foreach (var (index, frame) in batch)
                    CacheFrameStepRawFrame(index, frame);

                // Si el video termino antes de llegar al indice pedido (extremo del archivo), se
                // usa el ultimo cuadro que si se pudo decodificar.
                rawFrame = _frameStepCache.TryGetValue(targetIndex, out var exact) ? exact : batch[^1].Frame;
                targetIndex = _frameStepCache.ContainsKey(targetIndex) ? targetIndex : batch[^1].Index;
            }

            // El cache guarda bytes crudos, NO BitmapSource ya armados: convertir a BitmapSource
            // (copiar ~8MB y armar el bitmap) cuesta casi lo mismo que decodificar un cuadro entero.
            // Hacerlo para los ~25 cuadros del lote ANTES de mostrar el primero es lo que causaba la
            // lentitud del retroceso; aca se convierte UNICAMENTE el cuadro que se va a mostrar ahora.
            var bitmap = ToFrozenBitmapSource(rawFrame);

            _frameStepIndex = targetIndex;
            CurrentFrameNumber = targetIndex;
            FrameStepImage = bitmap;
            IsFrameStepActive = true;

            // A proposito NO se llama a MediaPlayer.Time aca: seekear a VLC cuesta lo mismo que el
            // retroceso por FFmpeg (decenas de ms) y haria lento CADA paso, incluso los que avanzan
            // (que sin esto son casi gratis, ver benchmark). VLC se resincroniza una sola vez, recien
            // al salir del modo (ExitFrameStep) — mientras tanto PositionMs es la fuente de verdad
            // (incluido el guardado periodico de progreso, ver SaveCurrentPositionAsync).
            var newPositionMs = targetIndex / _frameStepFps * 1000.0;
            PositionMs = newPositionMs;
            CurrentTimeText = FormatTime(newPositionMs);
        }
        catch
        {
            // Best effort: ante cualquier fallo de decodificacion se abandona el modo paso a cuadro
            // en vez de dejar la interfaz mostrando un cuadro congelado sin forma de salir.
            ExitFrameStep();
        }
        finally
        {
            _isFrameStepBusy = false;
        }
    }

    private void ExitFrameStep()
    {
        // Unico punto donde VLC se resincroniza: durante el paso a cuadro se lo dejo desactualizado
        // a proposito (ver StepFrameAsync) para que cada paso sea rapido. Se paga el costo del seek
        // una sola vez aca, no en cada cuadro revisado.
        if (IsFrameStepActive)
        {
            MediaPlayer.Time = (long)PositionMs;
            BeginSeekSettleWindow(PositionMs);
        }

        IsFrameStepActive = false;
        FrameStepImage = null;
    }

    /// <summary>Copia los pixeles de un cuadro recien decodificado a un <see cref="RawFrame"/> cacheable
    /// (la memoria de <see cref="ImageData"/> pertenece al decodificador y no sobrevive mas alla de la
    /// siguiente llamada, asi que hace falta copiarla para guardarla).</summary>
    private static RawFrame ToRawFrame(ImageData image) =>
        new(image.Data.ToArray(), image.ImageSize.Width, image.ImageSize.Height, image.Stride);

    /// <summary>
    /// Arma el BitmapSource de WPF a partir de un cuadro ya decodificado. Copiar el buffer y armar el
    /// bitmap cuesta un tiempo comparable al de decodificar el cuadro (~10ms en 1920x1080); por eso
    /// se llama UNA vez por cuadro mostrado, nunca por adelantado para todo un lote (ver StepFrameAsync).
    /// </summary>
    private static BitmapSource ToFrozenBitmapSource(RawFrame frame)
    {
        var bitmap = BitmapSource.Create(
            frame.Width,
            frame.Height,
            96, 96,
            System.Windows.Media.PixelFormats.Bgra32,
            null,
            frame.Pixels,
            frame.Stride);

        // Inmutable y seguro para asignar directamente al binding de FrameStepImage.
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>
    /// Decodifica un lote de cuadros consecutivos [<paramref name="fromIndex"/>, <paramref name="toIndex"/>]
    /// como bytes crudos (ver <see cref="RawFrame"/>): un unico seek al primero (GetFrame, el paso
    /// caro) y despues avance secuencial (TryGetNextFrame, casi gratis) hasta el destino. Corre en un
    /// hilo de fondo (Task.Run): metodo estatico, sin tocar el ViewModel.
    /// </summary>
    private static List<(int Index, RawFrame Frame)> DecodeFrameBatch(MediaFile file, double fps, int fromIndex, int toIndex)
    {
        var results = new List<(int Index, RawFrame Frame)>(toIndex - fromIndex + 1);

        var startTime = TimeSpan.FromSeconds((fromIndex + 0.5) / fps);
        var first = file.Video.GetFrame(startTime);
        results.Add((fromIndex, ToRawFrame(first)));

        for (var i = fromIndex + 1; i <= toIndex; i++)
        {
            // Fin del archivo u otro corte: el lote queda mas corto de lo pedido, el llamador usa
            // el ultimo cuadro que si se pudo decodificar.
            if (!file.Video.TryGetNextFrame(out var next))
                break;

            results.Add((i, ToRawFrame(next)));
        }

        return results;
    }

    /// <summary>Cache FIFO de cuadros ya decodificados (evita re-pagar el seek al revisitar la misma zona).</summary>
    private void CacheFrameStepRawFrame(int index, RawFrame frame)
    {
        if (!_frameStepCache.TryAdd(index, frame))
            return;

        _frameStepCacheOrder.Enqueue(index);
        _frameStepCacheBytes += frame.Pixels.LongLength;

        // Se desaloja en orden de llegada hasta volver al presupuesto, pero nunca el ultimo que
        // queda: el cuadro recien agregado es justamente el que se esta por mostrar.
        while (_frameStepCacheOrder.Count > 1 && _frameStepCacheBytes > FrameStepCacheBudgetBytes)
        {
            var evicted = _frameStepCacheOrder.Dequeue();
            if (_frameStepCache.Remove(evicted, out var removed))
                _frameStepCacheBytes -= removed.Pixels.LongLength;
        }
    }

    /// <summary>Posicion del video actual dentro de la lista de reproduccion (-1 si no esta).</summary>
    private int GetCurrentIndex()
    {
        if (CurrentVideo is null)
            return -1;

        for (var i = 0; i < _playlist.Count; i++)
        {
            if (_playlist[i].Id == CurrentVideo.Id)
                return i;
        }

        return -1;
    }

    private Video? GetNextVideo()
    {
        var index = GetCurrentIndex();
        return index >= 0 && index + 1 < _playlist.Count ? _playlist[index + 1] : null;
    }

    private Video? GetPreviousVideo()
    {
        var index = GetCurrentIndex();
        return index > 0 ? _playlist[index - 1] : null;
    }

    /// <summary>
    /// Durante los ultimos <see cref="NextFillWindowMs"/> ms el boton "Siguiente" aparece y se va
    /// rellenando; al llegar a cero el video termina y se encadena solo con el siguiente.
    /// </summary>
    private void UpdateNextFill()
    {
        // Con el loop activo el boton "Siguiente" no debe aparecer: se llegaria a el saltando de
        // vuelta al inicio del loop antes de que la ventana final del video se cumpla.
        if (IsLoopEnabled)
        {
            IsNextFillVisible = false;
            NextFillPercent = 0;
            return;
        }

        var remaining = DurationMs - PositionMs;

        if (!HasNextVideo || DurationMs <= 0 || remaining < 0 || remaining > NextFillWindowMs)
        {
            IsNextFillVisible = false;
            NextFillPercent = 0;
            return;
        }

        IsNextFillVisible = true;
        NextFillPercent = Math.Clamp((NextFillWindowMs - remaining) / NextFillWindowMs * 100.0, 0, 100);
    }

    [RelayCommand]
    private async Task PlayNextAsync()
    {
        var next = GetNextVideo();
        if (next is not null)
            await LoadVideoAsync(next);
    }

    [RelayCommand]
    private async Task PlayPreviousAsync()
    {
        var previous = GetPreviousVideo();
        if (previous is not null)
            await LoadVideoAsync(previous);
    }

    private async Task AdvanceToNextAsync()
    {
        // EndReached puede dispararse mas de una vez; el guard evita encadenar dos veces.
        if (_isAdvancing)
            return;

        var next = GetNextVideo();
        if (next is null)
        {
            IsNextFillVisible = false;
            NextFillPercent = 0;
            return;
        }

        _isAdvancing = true;
        try
        {
            _skipPositionSaveOnce = true;
            await LoadVideoAsync(next);
        }
        finally
        {
            _isAdvancing = false;
        }
    }

    [RelayCommand]
    private void PlayPause()
    {
        if (IsFrameStepActive)
        {
            // VLC ya esta pausado exactamente en esta posicion (se mantuvo sincronizado en cada
            // paso); alcanza con volver a mostrar su superficie y reanudar.
            ExitFrameStep();
            MediaPlayer.Play();
            return;
        }

        if (MediaPlayer.IsPlaying)
            MediaPlayer.Pause();
        else
            MediaPlayer.Play();
    }

    [RelayCommand]
    private async Task BackAsync()
    {
        await SaveCurrentPositionAsync();
        _saveTimer.Stop();

        // La ventana de referencia no tiene nada que mostrar sin video andando.
        _referenceWindow?.Close();

        await Task.Run(() => MediaPlayer.Stop());

        // El cache de cuadros puede tener cientos de MB: se libera al salir del reproductor en vez
        // de retenerlos mientras se navega la biblioteca.
        _frameStepCache.Clear();
        _frameStepCacheOrder.Clear();
        _frameStepCacheBytes = 0;

        _goBack();
    }

    [RelayCommand]
    private void ToggleLoop()
    {
        // Guarda necesaria desde que estos comandos son alcanzables por atajo de teclado: sin el
        // modo animador activo no hay forma de VER el loop/segmento, asi que tampoco deberian
        // poder activarse a ciegas con una tecla.
        if (!IsAnimatorModeEnabled)
            return;

        IsLoopEnabled = !IsLoopEnabled;
    }

    [RelayCommand]
    private void SetSegmentStart()
    {
        if (!IsAnimatorModeEnabled)
            return;

        // Si el nuevo IN queda pegado o despues del OUT existente, el OUT anterior ya no vale:
        // se limpia para que el usuario tenga que marcarlo de nuevo en vez de dejar un segmento invertido.
        if (SegmentEndMs.HasValue && PositionMs >= SegmentEndMs.Value - MinSegmentLengthMs)
            SegmentEndMs = null;

        SegmentStartMs = PositionMs;
    }

    [RelayCommand]
    private void SetSegmentEnd()
    {
        if (!IsAnimatorModeEnabled)
            return;

        // El OUT tiene que quedar claramente despues del IN; si no, se ignora en vez de crear un
        // segmento de largo casi nulo que rebotaria sin parar.
        if (SegmentStartMs.HasValue && PositionMs <= SegmentStartMs.Value + MinSegmentLengthMs)
            return;

        SegmentEndMs = PositionMs;
    }

    [RelayCommand]
    private void ClearSegment()
    {
        if (!IsAnimatorModeEnabled)
            return;

        SegmentStartMs = null;
        SegmentEndMs = null;
    }

    /// <summary>Ventana de referencia flotante abierta ahora mismo.</summary>
    private ReferenceWindow? _referenceWindow;

    [ObservableProperty]
    private bool isReferenceWindowOpen;

    /// <summary>
    /// Abre o cierra la ventana de referencia flotante. Comparte ESTE mismo ViewModel, asi que
    /// muestra el mismo video y responde a los mismos comandos (paso a cuadro, bucle) sin duplicar
    /// nada de la logica del reproductor.
    /// </summary>
    [RelayCommand]
    private void ToggleReferenceWindow()
    {
        // Requiere el render por callbacks (imagen WPF): con el video en la ventana nativa de LibVLC
        // no habria nada que mostrar aca, y ademas no obedeceria la transparencia.
        if (!IsAnimatorModeEnabled || !IsCallbackRenderingActive)
            return;

        if (_referenceWindow is not null)
        {
            _referenceWindow.Close();
            return;
        }

        _referenceWindow = new ReferenceWindow
        {
            DataContext = this,
            // Con dueno, se cierra sola al cerrar la app y nunca queda detras de la ventana principal.
            Owner = Application.Current?.MainWindow,
        };

        _referenceWindow.Closed += (_, _) =>
        {
            _referenceWindow = null;
            IsReferenceWindowOpen = false;
        };

        _referenceWindow.Show();
        IsReferenceWindowOpen = true;
    }

    [RelayCommand]
    private async Task AddMarkerAsync()
    {
        // Guarda necesaria porque este comando tambien es alcanzable por atajo de teclado (M).
        if (!IsAnimatorModeEnabled || CurrentVideo is null)
            return;

        var text = MarkerDialog.Prompt(string.Empty, Application.Current.MainWindow, Loc.I["Marker_NewTitle"]);
        if (text is null)
            return; // cancelado

        var timeMs = (long)PositionMs;
        var marker = await _markerService.AddMarkerAsync(CurrentVideo.Id, timeMs, text);
        var markerVm = new VideoMarkerViewModel(marker, (long)DurationMs);

        // Se inserta ordenada por tiempo (no necesariamente se agrega al final: se puede marcar en
        // cualquier punto del video, incluso antes de una etiqueta ya existente).
        var insertIndex = 0;
        while (insertIndex < Markers.Count && Markers[insertIndex].TimeMs <= markerVm.TimeMs)
            insertIndex++;
        Markers.Insert(insertIndex, markerVm);
    }

    [RelayCommand]
    private void JumpToMarker(VideoMarkerViewModel? marker)
    {
        if (marker is not null)
            SeekTo(marker.TimeMs);
    }

    [RelayCommand]
    private async Task EditMarkerAsync(VideoMarkerViewModel? marker)
    {
        if (marker is null)
            return;

        var text = MarkerDialog.Prompt(marker.Texto, Application.Current.MainWindow, Loc.I["Marker_EditTitle"]);
        if (text is null)
            return; // cancelado

        marker.Texto = text;
        await _markerService.UpdateMarkerTextAsync(marker.Id, text);
    }

    [RelayCommand]
    private async Task DeleteMarkerAsync(VideoMarkerViewModel? marker)
    {
        if (marker is null)
            return;

        Markers.Remove(marker);
        await _markerService.DeleteMarkerAsync(marker.Id);
    }

    /// <summary>
    /// El paso a cuadro necesita el fps y la cantidad de cuadros del video actual (sondeados en
    /// segundo plano al abrirlo, ver LoadVideoAsync); sin eso no hay como calcular a que cuadro
    /// corresponde la posicion actual. Los botones/atajos quedan deshabilitados hasta entonces: asi
    /// es imposible que el usuario dispare un paso con datos todavia incompletos o de otro video
    /// (evita el "cambio repentino" de numero de cuadro que se veia antes de esta guarda).
    /// </summary>
    private bool CanStepFrame() => IsAnimatorModeEnabled && _frameStepFile is not null && _frameStepFps > 0;

    /// <summary>
    /// Hay un video abierto en modo animador pero FFmpeg no pudo leerlo, asi que no hay paso a
    /// cuadro ni marcas de cuadro para el. El video igual se reproduce (de eso se encarga VLC, que
    /// trae su propio juego de codecs). Se expone para poder decirlo en pantalla: sin aviso, los
    /// botones aparecen apagados sin motivo visible y parece un error de la aplicacion.
    /// </summary>
    public bool IsFrameStepUnavailable => IsAnimatorModeEnabled && CurrentVideo is not null && !CanStepFrame();

    /// <summary>Refresca todo lo que depende de si el paso a cuadro esta disponible.</summary>
    private void NotifyFrameStepAvailabilityChanged()
    {
        StepFrameForwardCommand.NotifyCanExecuteChanged();
        StepFrameBackwardCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(IsFrameStepUnavailable));
    }

    [RelayCommand(CanExecute = nameof(CanStepFrame))]
    private async Task StepFrameForwardAsync() => await StepFrameAsync(1);

    [RelayCommand(CanExecute = nameof(CanStepFrame))]
    private async Task StepFrameBackwardAsync() => await StepFrameAsync(-1);

    // Cada cuanto (ms) se reenvia el seek a VLC mientras se arrastra la barra. Actualizar la perilla
    // es instantaneo, pero pedirle a VLC que decodifique el cuadro cuesta: reenviarlo en cada
    // MouseMove lo saturaria. ~60 ms (~16 Hz) alcanza para ver el video moverse con fluidez.
    private const long ScrubSeekThrottleMs = 60;

    /// <summary>Inicio del arrastre de la barra de progreso (ScrubBar en la vista).</summary>
    [RelayCommand]
    private void ScrubBegin()
    {
        _isSeekingFromUser = true;
        _lastScrubSeekTick = 0;

        // Arrastrar la barra es "buscar", no revisar cuadro a cuadro: se sale del modo paso a cuadro.
        if (IsFrameStepActive)
            ExitFrameStep();
    }

    /// <summary>
    /// Arrastre en curso: <paramref name="fraction"/> es 0..1 sobre el ancho de la barra. La perilla
    /// y el tiempo se actualizan al instante; el cuadro del video se refresca de forma limitada
    /// (throttle) para que se vea el cambio sin saturar a VLC.
    /// </summary>
    [RelayCommand]
    private void ScrubUpdate(double fraction)
    {
        if (DurationMs <= 0)
            return;

        var target = Math.Clamp(fraction, 0, 1) * DurationMs;
        PositionMs = target;
        CurrentTimeText = FormatTime(target);

        var now = Environment.TickCount64;
        if (now - _lastScrubSeekTick >= ScrubSeekThrottleMs)
        {
            _lastScrubSeekTick = now;
            MediaPlayer.Time = (long)target;
        }
    }

    /// <summary>Fin del arrastre: seek exacto al punto final y ventana de asentamiento.</summary>
    [RelayCommand]
    private void ScrubEnd(double fraction)
    {
        _isSeekingFromUser = false;
        SeekTo(Math.Clamp(fraction, 0, 1) * DurationMs);
    }

    /// <summary>Fija el volumen segun la fraccion 0..1 de la barra de volumen (ScrubBar en la vista).</summary>
    [RelayCommand]
    private void SetVolumeFraction(double fraction)
    {
        Volume = (int)Math.Round(Math.Clamp(fraction, 0, 1) * 100);
    }

    /// <summary>Salta a una posicion puntual (p.ej. clic en un marcador de la linea de tiempo): sale
    /// del paso a cuadro si estaba activo, mueve VLC y registra la ventana de asentamiento del seek.</summary>
    private void SeekTo(double targetMs)
    {
        if (IsFrameStepActive)
            ExitFrameStep();

        PositionMs = targetMs;
        MediaPlayer.Time = (long)targetMs;
        BeginSeekSettleWindow(targetMs);
        CurrentTimeText = FormatTime(targetMs);
    }

    partial void OnPositionMsChanged(double value) => OnPropertyChanged(nameof(ProgressPercent));

    partial void OnDurationMsChanged(double value)
    {
        OnPropertyChanged(nameof(ProgressPercent));
        OnPropertyChanged(nameof(SegmentStartPercent));
        OnPropertyChanged(nameof(SegmentBandWidthPercent));
        OnPropertyChanged(nameof(SegmentAfterPercent));
        OnPropertyChanged(nameof(SegmentStartTickIndex));
        OnPropertyChanged(nameof(SegmentEndTickIndex));
    }

    partial void OnSegmentStartMsChanged(double? value)
    {
        OnPropertyChanged(nameof(HasSegment));
        OnPropertyChanged(nameof(SegmentStartPercent));
        OnPropertyChanged(nameof(SegmentBandWidthPercent));
        OnPropertyChanged(nameof(SegmentRangeText));
        OnPropertyChanged(nameof(SegmentStartTickIndex));
    }

    partial void OnSegmentEndMsChanged(double? value)
    {
        OnPropertyChanged(nameof(HasSegment));
        OnPropertyChanged(nameof(SegmentBandWidthPercent));
        OnPropertyChanged(nameof(SegmentAfterPercent));
        OnPropertyChanged(nameof(SegmentRangeText));
        OnPropertyChanged(nameof(SegmentEndTickIndex));
    }

    partial void OnIsLoopEnabledChanged(bool value) => UpdateNextFill();

    [RelayCommand]
    private void ToggleMute()
    {
        if (Volume > 0)
        {
            _volumeBeforeMute = Volume;
            Volume = 0;
        }
        else
        {
            Volume = _volumeBeforeMute > 0 ? _volumeBeforeMute : 100;
        }
    }

    partial void OnVolumeChanged(int value)
    {
        MediaPlayer.Volume = value;
        OnPropertyChanged(nameof(IsMuted));
    }

    partial void OnSelectedPlaybackSpeedChanged(PlaybackSpeedOption value) => ApplyPlaybackSpeed();

    /// <summary>
    /// Aplica la velocidad al reproductor. SetRate devuelve distinto de 0 si falla (algunos
    /// formatos no admiten cambio de ritmo); en ese caso se vuelve a 1x para que la UI no muestre
    /// una velocidad que en realidad no se esta aplicando.
    /// </summary>
    private void ApplyPlaybackSpeed()
    {
        var target = SelectedPlaybackSpeed ?? PlaybackSpeedOption.Normal;

        if (MediaPlayer.SetRate(target.Rate) == 0)
            return;

        if (target.Rate != PlaybackSpeedOption.Normal.Rate)
            SelectedPlaybackSpeed = PlaybackSpeedOption.Normal;
    }

    private void RefreshSubtitleTracks()
    {
        SubtitleTracks.Clear();

        foreach (var track in MediaPlayer.SpuDescription ?? Array.Empty<LibVLCSharp.Shared.Structures.TrackDescription>())
        {
            // Id -1 es la entrada "Disable" que LibVLC agrega siempre a la lista; no es una pista real.
            if (track.Id == -1)
                continue;

            SubtitleTracks.Add(new SubtitleTrackOption(track.Id, string.IsNullOrWhiteSpace(track.Name) ? string.Format(Loc.I["Player_SubtitleTrackFallback"], track.Id) : track.Name));
        }

        HasSubtitleTracks = SubtitleTracks.Count > 0;
        HasMultipleSubtitleTracks = SubtitleTracks.Count > 1;

        var currentSpu = MediaPlayer.Spu;

        _isSyncingSubtitleSelection = true;
        SelectedSubtitleTrack = SubtitleTracks.FirstOrDefault(t => t.Id == currentSpu) ?? SubtitleTracks.FirstOrDefault();
        _isSyncingSubtitleSelection = false;

        SubtitlesEnabled = currentSpu != -1 && SubtitleTracks.Any(t => t.Id == currentSpu);
    }

    [RelayCommand]
    private void ToggleSubtitles()
    {
        if (!HasSubtitleTracks)
            return;

        if (SubtitlesEnabled)
        {
            MediaPlayer.SetSpu(-1);
        }
        else
        {
            var trackId = SelectedSubtitleTrack?.Id ?? SubtitleTracks.FirstOrDefault()?.Id;
            if (trackId is null)
                return;

            SetSpuWithRetry(trackId.Value);
        }

        SubtitlesEnabled = MediaPlayer.Spu != -1;
    }

    partial void OnSelectedSubtitleTrackChanged(SubtitleTrackOption? value)
    {
        if (_isSyncingSubtitleSelection || value is null)
            return;

        SetSpuWithRetry(value.Id);
        SubtitlesEnabled = MediaPlayer.Spu != -1;
    }

    // Tras deshabilitar subtitulos (Spu=-1), LibVLC a veces no reactiva la pista en el primer
    // llamado a SetSpu (el ES de subtitulos queda des-seleccionado internamente). Reintentar una
    // vez alcanza para destrabarlo; SubtitlesEnabled se fija segun el estado real, nunca asumido.
    private void SetSpuWithRetry(int trackId)
    {
        MediaPlayer.SetSpu(trackId);
        if (MediaPlayer.Spu != trackId)
            MediaPlayer.SetSpu(trackId);
    }

    private async Task SaveCurrentPositionAsync()
    {
        // Con el loop activo la posicion salta constantemente al inicio del loop: guardarla dejaria
        // el progreso registrado ahi en vez de donde el usuario realmente iba antes de activarlo.
        if (CurrentVideo is null || IsLoopEnabled)
            return;

        // Durante el paso a cuadro, MediaPlayer.Time queda desactualizado a proposito (no se
        // resincroniza con VLC en cada paso, ver StepFrameAsync/ExitFrameStep); PositionMs SI se
        // actualiza en cada cuadro, asi que es la fuente correcta mientras se esta revisando.
        var position = IsFrameStepActive
            ? (long)PositionMs
            : (MediaPlayer.Media is not null ? MediaPlayer.Time : (long)PositionMs);
        if (position < 0)
            position = (long)PositionMs;

        await _progressTracker.SaveProgressAsync(CurrentVideo.Id, position, (long)DurationMs);
    }

    private static string FormatTime(double ms) => TimeFormatter.Format(ms);

    public void SaveAndDispose()
    {
        if (CurrentVideo is not null)
            SaveCurrentPositionAsync().GetAwaiter().GetResult();

        Dispose();
    }

    public void Dispose()
    {
        _saveTimer.Stop();
        _opacitySaveTimer.Stop();
        MediaPlayer.Playing -= OnPlaying;
        MediaPlayer.Paused -= OnPausedOrStopped;
        MediaPlayer.Stopped -= OnPausedOrStopped;
        MediaPlayer.EndReached -= OnEndReached;
        MediaPlayer.TimeChanged -= OnTimeChanged;
        MediaPlayer.EncounteredError -= OnEncounteredError;
        MediaPlayer.Dispose();
        _media?.Dispose();
        _frameStepFile?.Dispose();
        _frameRenderer?.Dispose();
    }
}
