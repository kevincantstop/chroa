using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ChroaVideoPlayer.Core.EventBus;
using ChroaVideoPlayer.ViewModels;

namespace ChroaVideoPlayer.Controls;

public partial class MpvVideoPlayer : UserControl
{
    public static readonly StyledProperty<bool> IsFullScreenProperty =
        AvaloniaProperty.Register<MpvVideoPlayer, bool>(nameof(IsFullScreen));

    public static readonly StyledProperty<bool> IsTopMostProperty =
        AvaloniaProperty.Register<MpvVideoPlayer, bool>(nameof(IsTopMost));

    public VideoPlayerViewModel ViewModel { get; } = new();

    public IEventBus? Events { get; set; }

    public event Action? FullScreenToggleRequested;

    public event Action? TopMostToggleRequested;

    private const double SkipSeconds = 5;

    private readonly DispatcherTimer _copyFeedbackTimer = new() { Interval = TimeSpan.FromSeconds(1) };

    public MpvVideoPlayer()
    {
        DataContext = ViewModel;

        InitializeComponent();

        SeekBar.CanDrag = () => ViewModel.Duration > 0 && !ViewModel.HasEnded;
        SeekBar.Started += () => ViewModel.BeginSeek();
        SeekBar.Dragged += fraction => ViewModel.UpdatePosition(fraction * ViewModel.Duration);
        SeekBar.Committed += _ => ViewModel.CommitSeek();

        VolumeBar.CanDrag = () => ViewModel.HasVideo && ViewModel.HasAudioTrack;
        VolumeBar.Dragged += fraction => ViewModel.SetVolume(fraction * VideoPlayerViewModel.MaxVolume);

        SeekBar.Started += OnBarDragStarted;
        SeekBar.Committed += OnBarDragCommitted;
        VolumeBar.Started += OnBarDragStarted;
        VolumeBar.Committed += OnBarDragCommitted;

        ViewModel.PropertyChanged += OnViewModelPropertyChanged;

        ViewModel.Mpv.MediaOpened += OnMediaOpened;

        _copyFeedbackTimer.Tick += OnCopyFeedbackTick;

        ApplyState();
    }

    public bool IsFullScreen
    {
        get => GetValue(IsFullScreenProperty);
        set => SetValue(IsFullScreenProperty, value);
    }

    public bool IsTopMost
    {
        get => GetValue(IsTopMostProperty);
        set => SetValue(IsTopMostProperty, value);
    }

    public void Play(string path)
    {
        ViewModel.Mpv.ViewContext = MpvViewControl.MpvContext;

        ViewModel.Play(path);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == IsFullScreenProperty) ApplyState();
        else if (change.Property == IsTopMostProperty) ApplyState();
    }

    private void OnBarDragStarted() => ViewModel.IsDragging = true;

    private void OnBarDragCommitted(double fraction) => ViewModel.IsDragging = false;

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e) => ApplyState();

    private void OnMediaOpened(object? sender, EventArgs e)
    {
        var path = ViewModel.Mpv.CurrentFile;

        if (string.IsNullOrEmpty(path)) return;

        Events?.Publish(new VideoPlayed(path));
    }

    private void OnPlayerPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!ViewModel.IsSubtitleMenuOpen && !ViewModel.IsSpeedMenuOpen) return;

        if (e.Source is Visual source && source.FindAncestorOfType<Button>(true) is not null) return;

        ViewModel.CloseMenus();
    }

    private void ApplyState()
    {
        SeekBar.SetFraction(SeekFraction);
        VolumeBar.SetFraction(VolumeFraction);

        PositionText.Text = Format(ViewModel.Position);
        DurationText.Text = ViewModel.Duration > 0 ? Format(ViewModel.Duration) : "--:--";

        PlayButton.IsVisible = ViewModel.IsPaused || ViewModel.HasEnded;
        PauseButton.IsVisible = !PlayButton.IsVisible;

        VolumeButton.IsVisible = ViewModel.HasAudioTrack && !ViewModel.IsMuted;
        MutedButton.IsVisible = !VolumeButton.IsVisible;

        FullScreenButton.IsVisible = !IsFullScreen;
        FullScreenExitButton.IsVisible = IsFullScreen;

        TopMostButton.IsVisible = !IsTopMost;
        TopMostOnButton.IsVisible = IsTopMost;

        AspectResizeButton.IsVisible = !ViewModel.IsFreeResize;
        FreeResizeButton.IsVisible = ViewModel.IsFreeResize;

        var canSkip = ViewModel.Duration > 0;

        RewindButton.IsEnabled = canSkip;
        ForwardButton.IsEnabled = canSkip;

        SpeedMenu.ApplyState(ViewModel.Speed);

        var speedLabel = FormatSpeed(ViewModel.Speed);

        SpeedText.Text = speedLabel;
        SpeedText.IsVisible = speedLabel.Length > 0;
    }

    private double DurationMaximum => ViewModel.Duration > 0 ? ViewModel.Duration : 1;

    private double SeekFraction => Math.Clamp(ViewModel.Position / DurationMaximum, 0, 1);

    private double VolumeFraction => Math.Clamp(ViewModel.Volume / VideoPlayerViewModel.MaxVolume, 0, 1);

    private static string Format(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0) seconds = 0;

        var t = TimeSpan.FromSeconds(seconds);
        return t.TotalHours >= 1
            ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}"
            : $"{t.Minutes:00}:{t.Seconds:00}";
    }

    private static string FormatSpeed(double speed) =>
        Math.Abs(speed - 1) < 0.001 ? string.Empty : speed.ToString("0.#", CultureInfo.InvariantCulture) + "×";

    private void OnPlayPauseClick(object? sender, RoutedEventArgs e) => ViewModel.Mpv.TogglePause();

    private void OnMuteClick(object? sender, RoutedEventArgs e) => ViewModel.Mpv.ToggleMute();

    private void OnSubtitleClick(object? sender, RoutedEventArgs e) => ViewModel.ToggleSubtitleMenu();

    private void OnSpeedClick(object? sender, RoutedEventArgs e) => ViewModel.ToggleSpeedMenu();

    private void OnRewindClick(object? sender, RoutedEventArgs e) => ViewModel.Mpv.SeekBy(-SkipSeconds);

    private void OnForwardClick(object? sender, RoutedEventArgs e) => ViewModel.Mpv.SeekBy(SkipSeconds);

    private void OnFullScreenClick(object? sender, RoutedEventArgs e) => FullScreenToggleRequested?.Invoke();

    private void OnTopMostClick(object? sender, RoutedEventArgs e) => TopMostToggleRequested?.Invoke();

    private void OnResizeModeClick(object? sender, RoutedEventArgs e) => ViewModel.IsFreeResize = !ViewModel.IsFreeResize;

    private async void OnCopyFrameClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard is null) return;

            var bytes = await ViewModel.Mpv.CaptureFrameAsync();
            if (bytes is null || bytes.Length == 0) return;

            using var stream = new MemoryStream(bytes);
            var bitmap = new Bitmap(stream);

            await clipboard.SetBitmapAsync(bitmap);

            await clipboard.FlushAsync();

            StartCopyFeedback();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Copy frame failed: {ex}");
        }
    }

    private void StartCopyFeedback()
    {
        CopyFrameButton.IsVisible = false;
        CopiedButton.IsVisible = true;

        _copyFeedbackTimer.Stop();
        _copyFeedbackTimer.Start();
    }

    private void OnCopyFeedbackTick(object? sender, EventArgs e)
    {
        _copyFeedbackTimer.Stop();

        CopyFrameButton.IsVisible = true;
        CopiedButton.IsVisible = false;
    }
}
