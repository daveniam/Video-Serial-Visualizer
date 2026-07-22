using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LibVLCSharp.Shared;
using VideoSerialVisualizer.Helpers;
using VideoSerialVisualizer.Models;
using VideoSerialVisualizer.Services;

namespace VideoSerialVisualizer.ViewModels;

public partial class PlayerViewModel : ObservableObject, IDisposable
{
    private readonly LibVLC _libVlc;
    private readonly ProgressTrackerService _progressTracker;
    private readonly Action _goBack;
    private readonly DispatcherTimer _saveTimer;

    private Media? _media;
    private bool _hasAppliedSavedPosition;
    private bool _isSeekingFromUser;
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

    private IReadOnlyList<Video> _playlist = Array.Empty<Video>();
    private bool _isAdvancing;
    private bool _skipPositionSaveOnce;

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

    /// <summary>El boton "Siguiente" se muestra recien en los ultimos segundos del video.</summary>
    [ObservableProperty]
    private bool isNextFillVisible;

    /// <summary>0-100: cuanto se lleno el boton "Siguiente" dentro de la ventana de 30s final.</summary>
    [ObservableProperty]
    private double nextFillPercent;

    public PlayerViewModel(LibVLC libVlc, ProgressTrackerService progressTracker, Action goBack)
    {
        _libVlc = libVlc;
        _progressTracker = progressTracker;
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

        CurrentVideo = video;

        HasNextVideo = GetNextVideo() is not null;
        HasPreviousVideo = GetPreviousVideo() is not null;
        IsNextFillVisible = false;
        NextFillPercent = 0;

        _hasAppliedSavedPosition = false;
        _hasLoadedSubtitleTracks = false;
        SubtitleTracks.Clear();
        HasSubtitleTracks = false;
        HasMultipleSubtitleTracks = false;
        SubtitlesEnabled = false;
        SelectedSubtitleTrack = null;

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

        // Reafirmar el Hwnd justo antes de reproducir: si LibVLC no tiene una ventana de destino
        // al hacer Play, crea la suya propia (video flotante en una ventana aparte).
        if (_videoHwnd != IntPtr.Zero)
            MediaPlayer.Hwnd = _videoHwnd;

        MediaPlayer.Play(_media);
        MediaPlayer.Volume = Volume;
        _saveTimer.Start();
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

            if (!_hasAppliedSavedPosition)
            {
                _hasAppliedSavedPosition = true;
                if (PositionMs > 0)
                    MediaPlayer.Time = (long)PositionMs;
            }

            if (!_hasLoadedSubtitleTracks)
            {
                _hasLoadedSubtitleTracks = true;
                RefreshSubtitleTracks();
            }
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
                $"No se pudo reproducir \"{nombre}\". El archivo puede estar dañado o tener un formato/códec no soportado.",
                "Error de reproducción",
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

            PositionMs = e.Time;
            CurrentTimeText = FormatTime(e.Time);
            UpdateNextFill();
        });
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
        await Task.Run(() => MediaPlayer.Stop());
        _goBack();
    }

    public void BeginUserSeek() => _isSeekingFromUser = true;

    public void EndUserSeek(double newPositionMs)
    {
        _isSeekingFromUser = false;
        PositionMs = newPositionMs;
        MediaPlayer.Time = (long)newPositionMs;
        CurrentTimeText = FormatTime((long)newPositionMs);
    }

    partial void OnPositionMsChanged(double value) => OnPropertyChanged(nameof(ProgressPercent));

    partial void OnDurationMsChanged(double value) => OnPropertyChanged(nameof(ProgressPercent));

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

    private void RefreshSubtitleTracks()
    {
        SubtitleTracks.Clear();

        foreach (var track in MediaPlayer.SpuDescription ?? Array.Empty<LibVLCSharp.Shared.Structures.TrackDescription>())
        {
            // Id -1 es la entrada "Disable" que LibVLC agrega siempre a la lista; no es una pista real.
            if (track.Id == -1)
                continue;

            SubtitleTracks.Add(new SubtitleTrackOption(track.Id, string.IsNullOrWhiteSpace(track.Name) ? $"Pista {track.Id}" : track.Name));
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
        if (CurrentVideo is null)
            return;

        var position = MediaPlayer.Media is not null ? MediaPlayer.Time : (long)PositionMs;
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
        MediaPlayer.Playing -= OnPlaying;
        MediaPlayer.Paused -= OnPausedOrStopped;
        MediaPlayer.Stopped -= OnPausedOrStopped;
        MediaPlayer.EndReached -= OnEndReached;
        MediaPlayer.TimeChanged -= OnTimeChanged;
        MediaPlayer.EncounteredError -= OnEncounteredError;
        MediaPlayer.Dispose();
        _media?.Dispose();
    }
}
