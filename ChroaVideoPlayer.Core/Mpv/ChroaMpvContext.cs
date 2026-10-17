using System;
using System.Diagnostics;
using System.Threading.Tasks;
using HanumanInstitute.LibMpv;
using HanumanInstitute.LibMpv.Core;

namespace ChroaVideoPlayer.Core.Mpv;

public readonly record struct PlaybackProgress(double Position, double Duration);

public sealed partial class ChroaMpvContext
{
    private MpvContext? _context;
    private string? _pendingFile;
    private bool _aspectReported;

    private bool _sizeTrustworthy;

    private const int ProgressPublishIntervalMs = 200;

    private readonly Stopwatch _progressClock = Stopwatch.StartNew();
    private double _position;
    private double _duration;

    private string? _currentFile;

    private bool _ended;

    private const string HardwareDecodingMode = "auto-safe";

    private const string NativeLibraryFolder = "native";

    public static void ConfigureNativeLibrary(string baseDirectory) =>
        MpvApi.RootPath = System.IO.Path.Combine(baseDirectory, NativeLibraryFolder);

    public string? CurrentFile => _currentFile;

    public MpvContext? ViewContext
    {
        get => _context;
        set
        {
            if (ReferenceEquals(_context, value)) return;

            var previous = _context;
            _context = value;

            SafeRun(nameof(Detach), () => Detach(previous));
            SafeRun(nameof(Attach), () => Attach(value));
        }
    }

    public event EventHandler<double>? VideoAspectDetected;

    public event EventHandler<bool>? PauseChanged;

    public event EventHandler? MediaOpened;

    public event EventHandler? MediaEnded;

    public event EventHandler<PlaybackProgress>? ProgressChanged;

    public event EventHandler<bool>? EndedChanged;

    public void Play(string path)
    {
        _currentFile = path;

        var context = _context;
        if (context is null)
        {
            Debug.WriteLine($"mpv context not ready yet; queued until it attaches: {path}");
            _pendingFile = path;
            return;
        }

        _ = PlayAsync(context, path);
    }

    public void TogglePause()
    {
        var context = _context;
        if (context is null) return;

        if (_ended && _currentFile is not null)
        {
            Play(_currentFile);
            return;
        }

        SafeRun(nameof(TogglePause), () => context.Pause.Set(!(context.Pause.Get() ?? false)));
    }

    public void SeekTo(double seconds)
    {
        var context = _context;
        if (context is null || !double.IsFinite(seconds) || seconds < 0) return;

        SafeRun(nameof(SeekTo), () =>
        {
            _position = seconds;
            PublishProgress(force: true);

            _ = context.Seek(seconds, SeekOption.Absolute).InvokeAsync();
        });
    }

    public void SeekBy(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds == 0) return;

        var target = _position + seconds;
        if (target < 0) target = 0;
        if (_duration > 0 && target > _duration) target = _duration;

        SeekTo(target);
    }

    private static async Task PlayAsync(MpvContext context, string path)
    {
        try
        {
            var waited = 0;
            while (!context.IsCustomRendering() && waited < 5000)
            {
                await Task.Delay(50);
                waited += 50;
            }

            if (!context.IsCustomRendering())
            {
                Debug.WriteLine("OpenGL render context was not created within 5 seconds; no video output (check ANGLE / RenderingMode)");
            }

            await context.LoadFile(path).InvokeAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"LoadFile failed: {ex}");
        }
    }

    private void Attach(MpvContext? context)
    {
        if (context is null) return;

        TryEnableHardwareDecoding(context);

        _aspectReported = false;
        _sizeTrustworthy = false;
        context.StartFile += OnStartFile;
        context.FileLoaded += OnFileLoaded;
        context.VideoReconfig += OnVideoReconfig;
        context.Pause.Changed += OnPausePropertyChanged;
        context.TimePos.Changed += OnTimePosChanged;
        context.Duration.Changed += OnDurationChanged;
        context.SeekRaised += OnSeekRaised;
        context.Volume.Changed += OnVolumePropertyChanged;
        context.Mute.Changed += OnMutePropertyChanged;
        context.Speed.Changed += OnSpeedPropertyChanged;
        context.EndFile += OnEndFile;
        context.EofReached.Changed += OnEofReachedChanged;
        context.AudioReconfig += OnAudioReconfig;

        var pending = _pendingFile;
        if (pending is not null)
        {
            _pendingFile = null;
            _ = PlayAsync(context, pending);
        }

        TryApplyStoredOptions(context);

        TryPublishVolume();
        TryPublishSpeed();
    }

    private void Detach(MpvContext? context)
    {
        if (context is null) return;

        context.StartFile -= OnStartFile;
        context.FileLoaded -= OnFileLoaded;
        context.VideoReconfig -= OnVideoReconfig;
        context.Pause.Changed -= OnPausePropertyChanged;
        context.TimePos.Changed -= OnTimePosChanged;
        context.Duration.Changed -= OnDurationChanged;
        context.SeekRaised -= OnSeekRaised;
        context.Volume.Changed -= OnVolumePropertyChanged;
        context.Mute.Changed -= OnMutePropertyChanged;
        context.Speed.Changed -= OnSpeedPropertyChanged;
        context.EndFile -= OnEndFile;
        context.EofReached.Changed -= OnEofReachedChanged;
        context.AudioReconfig -= OnAudioReconfig;
    }

    private static void TryEnableHardwareDecoding(MpvContext context)
    {
        SafeRun("TryEnableHardwareDecoding", () => {
            context.Option["hwdec"].Set(HardwareDecodingMode);
        });
    }

    private void TryApplyStoredOptions(MpvContext context) =>
        SafeRun(nameof(TryApplyStoredOptions), () => {
            context.Volume.Set(_volume);
            context.Speed.Set(_speed);
        });



    private void OnStartFile(object? sender, MpvStartFileEventArgs e) =>
        SafeRun(nameof(OnStartFile), () =>
        {
            _aspectReported = false;
            _sizeTrustworthy = false;

            _position = 0;
            _duration = 0;
            PublishProgress(force: true);

            SetEnded(false);

            PublishSubtitles(NoSubtitles);
        });

    private void OnVideoReconfig(object? sender, EventArgs e) =>
        SafeRun(nameof(OnVideoReconfig), () =>
        {
            _sizeTrustworthy = true;
            TryReportAspect();
        });

    private void OnFileLoaded(object? sender, EventArgs e) =>
        SafeRun(nameof(OnFileLoaded), () =>
        {
            TryPublishVolume();

            _ = RefreshAudioTrackAsync();
            _ = RefreshSubtitlesAsync();

            EnsurePlaying();

            MediaOpened?.Invoke(this, EventArgs.Empty);

            if (_sizeTrustworthy) TryReportAspect();
            else _ = RetryAspectAsync();
        });

    private void OnPausePropertyChanged(object? sender, MpvValueChangedEventArgs<bool, bool> e) =>
        SafeRun(nameof(OnPausePropertyChanged), () =>
        {
            PauseChanged?.Invoke(this, e.NewValue ?? false);

            PublishProgress(force: true);
        });

    private void TryReportAspect()
    {
        if (_aspectReported) return;

        if (!_sizeTrustworthy) return;

        var context = _context;
        if (context is null) return;

        int width, height;
        try
        {
            width = context.DisplayWidth.Get() ?? 0;
            height = context.DisplayHeight.Get() ?? 0;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"aspect not ready yet ({ex.GetType().Name})");
            _ = RetryAspectAsync();
            return;
        }

        if (width <= 0 || height <= 0) return;

        _aspectReported = true;
        VideoAspectDetected?.Invoke(this, (double)width / height);
    }

    private async Task RetryAspectAsync()
    {
        for (var i = 0; i < 20 && !_aspectReported; i++)
        {
            await Task.Delay(100);

            if (i == 10) _sizeTrustworthy = true;

            SafeRun(nameof(RetryAspectAsync), TryReportAspect);
        }
    }


    private void OnTimePosChanged(object? sender, MpvValueChangedEventArgs<double, double> e) =>
        SafeRun(nameof(OnTimePosChanged), () =>
        {
            if (e.NewValue is { } v && double.IsFinite(v) && v >= 0) _position = v;
            PublishProgress(force: false);
        });

    private void OnDurationChanged(object? sender, MpvValueChangedEventArgs<double, double> e) =>
        SafeRun(nameof(OnDurationChanged), () =>
        {
            if (e.NewValue is { } v && double.IsFinite(v) && v >= 0) _duration = v;

            PublishProgress(force: true);
        });

    private void OnSeekRaised(object? sender, EventArgs e) =>
        SafeRun(nameof(OnSeekRaised), () => PublishProgress(force: true));

    private void PublishProgress(bool force)
    {
        if (!force && _progressClock.ElapsedMilliseconds < ProgressPublishIntervalMs) return;

        _progressClock.Restart();
        ProgressChanged?.Invoke(this, new PlaybackProgress(_position, _duration));
    }


    private void OnEndFile(object? sender, MpvEndFileEventArgs e) =>
        SafeRun(nameof(OnEndFile), () =>
        {
            SetEnded(true);

            if (e.Reason == MpvEndFileReason.EndOfFile) MediaEnded?.Invoke(this, EventArgs.Empty);
        });

    private void EnsurePlaying()
    {
        var context = _context;

        if (context is null) return;

        SafeRun(nameof(EnsurePlaying), () =>
        {
            if (context.Pause.Get() == true) context.Pause.Set(false);
        });
    }

    private void OnEofReachedChanged(object? sender, MpvValueChangedEventArgs<bool, bool> e) =>
        SafeRun(nameof(OnEofReachedChanged), () =>
        {
            if (e.NewValue == true) SetEnded(true);
        });

    private void SetEnded(bool ended)
    {
        if (_ended == ended) return;

        _ended = ended;
        EndedChanged?.Invoke(this, ended);
    }

    private static void SafeRun(string name, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"mpv handler {name} threw: {ex}");
        }
    }
}
