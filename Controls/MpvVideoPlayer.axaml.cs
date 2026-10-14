using System;
using System.ComponentModel;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using ChroaVideoPlayer.ViewModels;

namespace ChroaVideoPlayer.Controls;

public partial class MpvVideoPlayer : UserControl
{
    public static readonly StyledProperty<bool> IsFullScreenProperty =
        AvaloniaProperty.Register<MpvVideoPlayer, bool>(nameof(IsFullScreen));

    public VideoPlayerViewModel ViewModel { get; } = new();

    public event Action? FullScreenToggleRequested;

    private const double SkipSeconds = 5;

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

        ApplyState();
    }

    public bool IsFullScreen
    {
        get => GetValue(IsFullScreenProperty);
        set => SetValue(IsFullScreenProperty, value);
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
    }

    private void OnBarDragStarted() => ViewModel.IsDragging = true;

    private void OnBarDragCommitted(double fraction) => ViewModel.IsDragging = false;

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e) => ApplyState();

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
}
