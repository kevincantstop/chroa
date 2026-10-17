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
    public const double PreviewWidth = 320;

    public const double PreviewHeight = 180;

    private readonly ClipCaptureManager _manager = new();

    private readonly DispatcherTimer _playbackTimer = new();

    private readonly List<Bitmap> _frames = new();

    private CancellationTokenSource? _capture;

    private int _frameIndex;

    private string? _path;

    private TimeSpan _captureAt = TimeSpan.FromSeconds(-1);

    public ClipCapture()
    {
        InitializeComponent();

        Width = PreviewWidth;
        Height = PreviewHeight;

        _playbackTimer.Tick += OnPlaybackTick;
    }

    public TimeSpan PreviewLength { get; set; } = TimeSpan.FromSeconds(1);

    public void Show(string path, TimeSpan captureAt)
    {
        if (_frames.Count > 0 && _path == path && _captureAt == captureAt) return;

        _path = path;
        _captureAt = captureAt;

        TimeText.Text = Format(captureAt);

        _ = RefreshAsync();
    }

    public void Hide()
    {
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

    private async Task RefreshAsync()
    {
        Cancel();

        if (_path is not { } path || _captureAt < TimeSpan.Zero) return;

        var source = new CancellationTokenSource();

        _capture = source;

        try
        {
            var result = await _manager.CaptureAsync(path, _captureAt, PreviewLength, source.Token);

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

        if (_frames.Count == 1 || result.FramesPerSecond <= 0) return;

        _playbackTimer.Interval = TimeSpan.FromSeconds(1d / result.FramesPerSecond);
        _playbackTimer.Start();
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
