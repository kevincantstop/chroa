using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ChroaVideoPlayer.Core.ClipCapture;

namespace ChroaVideoPlayer.Controls;

public partial class ClipCapture : UserControl
{
    public const double ShadowMargin = 24;

    private const double PreviewWidth = 320;

    private const double PreviewHeight = 180;

    private readonly ClipCaptureManager _manager = new();

    private readonly DispatcherTimer _playbackTimer = new();

    private readonly DispatcherTimer _dwellTimer = new();

    private readonly List<Bitmap> _frames = new();

    private CancellationTokenSource? _capture;

    private int _frameIndex;

    private string? _path;

    private TimeSpan _captureAt = TimeSpan.FromSeconds(-1);

    public ClipCapture()
    {
        InitializeComponent();

        Width = PreviewWidth + ShadowMargin * 2;
        Height = PreviewHeight + ShadowMargin * 2;

        _playbackTimer.Tick += OnPlaybackTick;
        _dwellTimer.Tick += OnDwellTick;
    }

    public event Action? FrameReady;

    public TimeSpan PreviewLength { get; set; } = TimeSpan.FromSeconds(1);

    public TimeSpan DwellLength { get; set; } = TimeSpan.FromSeconds(1);

    public void Show(string path, TimeSpan captureAt)
    {
        if (_path == path && _captureAt == captureAt && _dwellTimer.IsEnabled) return;

        _path = path;
        _captureAt = captureAt;

        TimeText.Text = Format(captureAt);

        _dwellTimer.Stop();

        _ = RefreshAsync(TimeSpan.Zero);

        if (PreviewLength <= TimeSpan.Zero) return;

        _dwellTimer.Interval = DwellLength;
        _dwellTimer.Start();
    }

    public void ClearCache()
    {
        Cancel();
        ClearFrames();

        _path = null;
        _captureAt = TimeSpan.FromSeconds(-1);

        ClipCaptureManager.ClearCache();
    }

    public void Hide()
    {
        _dwellTimer.Stop();

        Cancel();

        _path = null;
        _captureAt = TimeSpan.FromSeconds(-1);

        ClearFrames();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);

        Hide();
    }

    private void OnDwellTick(object? sender, EventArgs e)
    {
        _dwellTimer.Stop();

        _ = RefreshAsync(PreviewLength);
    }

    private async Task RefreshAsync(TimeSpan length)
    {
        Cancel();

        if (_path is not { } path || _captureAt < TimeSpan.Zero) return;

        var source = new CancellationTokenSource();

        _capture = source;

        try
        {
            var result = await _manager.CaptureAsync(path, _captureAt, length, source.Token);

            if (result is null || source.IsCancellationRequested) return;

            Load(result);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Preview capture failed: {ex}");
        }
    }

    private void Load(ClipCaptureResult result)
    {
        ClearFrames();

        foreach (var frame in result.Frames)
        {
            try
            {
                _frames.Add(new Bitmap(frame));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Could not load a preview frame: {ex.Message}");
            }
        }

        if (_frames.Count == 0) return;

        _frameIndex = 0;
        Frame.Source = _frames[0];

        ApplyFrameHeight();

        FrameReady?.Invoke();

        if (_frames.Count == 1 || result.FramesPerSecond <= 0) return;

        _playbackTimer.Interval = TimeSpan.FromSeconds(1d / result.FramesPerSecond);
        _playbackTimer.Start();
    }

    private void ApplyFrameHeight()
    {
        if (_frames.Count == 0) return;

        var size = _frames[0].PixelSize;

        if (size.Width <= 0 || size.Height <= 0) return;

        Height = Math.Round(PreviewWidth * size.Height / size.Width) + ShadowMargin * 2;
    }

    private void OnPlaybackTick(object? sender, EventArgs e)
    {
        if (_frames.Count == 0) return;

        _frameIndex = (_frameIndex + 1) % _frames.Count;

        Frame.Source = _frames[_frameIndex];
    }

    private void ClearFrames()
    {
        _playbackTimer.Stop();

        Frame.Source = null;

        foreach (var frame in _frames) frame.Dispose();

        _frames.Clear();
    }

    private void Cancel()
    {
        _capture?.Cancel();
        _capture?.Dispose();
        _capture = null;
    }

    private static string Format(TimeSpan value)
    {
        if (value < TimeSpan.Zero) value = TimeSpan.Zero;

        return value.TotalHours >= 1
            ? $"{(int)value.TotalHours}:{value.Minutes:00}:{value.Seconds:00}"
            : $"{value.Minutes:00}:{value.Seconds:00}";
    }
}
