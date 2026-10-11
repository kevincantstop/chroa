using System;
using System.Diagnostics;
using System.Threading.Tasks;
using HanumanInstitute.LibMpv;

namespace ChroaVideoPlayer.Mpv;

public readonly record struct PlaybackProgress(double Position, double Duration);

public readonly record struct PlaybackVolume(double Volume, bool Muted);

public sealed class ChroaMpvContext
{
    private MpvContext? _context;
    private string? _pendingFile;
    private bool _aspectReported;

    private bool _sizeTrustworthy;

    private const int ProgressPublishIntervalMs = 200;

    private readonly Stopwatch _progressClock = Stopwatch.StartNew();
    private double _position;
    private double _duration;

    private double _volume = 100;
    private bool _muted;

    private string? _currentFile;

    private bool _ended;

    private bool _hasAudio = true;

    private const string HardwareDecodingMode = "auto-safe";

    public MpvContext? ViewContext
    {
        get => _context;
        set
        {
            if (ReferenceEquals(_context, value)) return;

            Detach(_context);
            _context = value;
            Attach(value);
        }
    }

    public event EventHandler<double>? VideoAspectDetected;

    public event EventHandler<bool>? PauseChanged;

    public event EventHandler? MediaOpened;

    public event EventHandler<PlaybackProgress>? ProgressChanged;

    public event EventHandler<PlaybackVolume>? VolumeChanged;

    public event EventHandler<bool>? EndedChanged;

    public event EventHandler<bool>? AudioTrackChanged;

    public void Play(string path)
    {
        _currentFile = path;

        var context = _context;
        if (context is null)
        {
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

        try
        {
            context.Pause.Set(!(context.Pause.Get() ?? false));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"TogglePause failed: {ex}");
        }
    }

    public void SeekTo(double seconds)
    {
        var context = _context;
        if (context is null || !double.IsFinite(seconds) || seconds < 0) return;

        try
        {
            _position = seconds;
            PublishProgress(force: true);

            _ = context.Seek(seconds, SeekOption.Absolute).InvokeAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Seek failed: {ex}");
        }
    }

    public void SetVolume(double volume)
    {
        var context = _context;
        if (context is null || !double.IsFinite(volume) || volume < 0) return;

        try
        {
            _volume = volume;

            if (_muted && volume > 0)
            {
                _muted = false;
                context.Mute.Set(false);
            }

            PublishVolume();
            context.Volume.Set(volume);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"SetVolume failed: {ex}");
        }
    }

    public void ToggleMute()
    {
        var context = _context;
        if (context is null) return;

        try
        {
            _muted = !(context.Mute.Get() ?? false);
            context.Mute.Set(_muted);
            PublishVolume();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ToggleMute failed: {ex}");
        }
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
        context.EndFile += OnEndFile;
        context.EofReached.Changed += OnEofReachedChanged;
        context.AudioReconfig += OnAudioReconfig;

        var pending = _pendingFile;
        if (pending is not null)
        {
            _pendingFile = null;
            _ = PlayAsync(context, pending);
        }

        TryPublishVolume();
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
        context.EndFile -= OnEndFile;
        context.EofReached.Changed -= OnEofReachedChanged;
        context.AudioReconfig -= OnAudioReconfig;
    }

    private static void TryEnableHardwareDecoding(MpvContext context)
    {
        try
        {
            context.Option["hwdec"].Set(HardwareDecodingMode);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to set hwdec={HardwareDecodingMode}; falling back to software decoding: {ex.Message}");
        }
    }


    private void OnStartFile(object? sender, MpvStartFileEventArgs e) =>
        SafeRun(nameof(OnStartFile), () =>
        {
            _aspectReported = false;
            _sizeTrustworthy = false;

            _position = 0;
            _duration = 0;
            PublishProgress(force: true);

            SetEnded(false);
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


    private void OnVolumePropertyChanged(object? sender, MpvValueChangedEventArgs<double, double> e) =>
        SafeRun(nameof(OnVolumePropertyChanged), () =>
        {
            if (e.NewValue is { } v && double.IsFinite(v)) _volume = v;
            PublishVolume();
        });

    private void OnMutePropertyChanged(object? sender, MpvValueChangedEventArgs<bool, bool> e) =>
        SafeRun(nameof(OnMutePropertyChanged), () =>
        {
            _muted = e.NewValue ?? false;
            PublishVolume();
        });

    private void PublishVolume() => VolumeChanged?.Invoke(this, new PlaybackVolume(_volume, _muted));

    private void TryPublishVolume()
    {
        var context = _context;
        if (context is null) return;

        try
        {
            _volume = context.Volume.Get() ?? _volume;
            _muted = context.Mute.Get() ?? _muted;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"volume/mute not ready yet ({ex.GetType().Name})");
            return;
        }

        PublishVolume();
    }


    private void OnAudioReconfig(object? sender, EventArgs e) =>
        SafeRun(nameof(OnAudioReconfig), () => SetHasAudio(true));

    private async Task RefreshAudioTrackAsync()
    {
        for (var i = 0; i < 10; i++)
        {
            if (TryReadHasAudio(out var hasAudio))
            {
                SetHasAudio(hasAudio);
                return;
            }

            await Task.Delay(100);
        }
    }

    private bool TryReadHasAudio(out bool hasAudio)
    {
        hasAudio = true;

        var context = _context;
        if (context is null) return false;

        try
        {
            hasAudio = context.AudioId.Get() is not null;
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"aid not ready yet ({ex.GetType().Name})");
            return false;
        }
    }

    private void SetHasAudio(bool hasAudio)
    {
        if (_hasAudio == hasAudio) return;

        _hasAudio = hasAudio;
        AudioTrackChanged?.Invoke(this, hasAudio);
    }


    private void OnEndFile(object? sender, MpvEndFileEventArgs e) =>
        SafeRun(nameof(OnEndFile), () => SetEnded(true));

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