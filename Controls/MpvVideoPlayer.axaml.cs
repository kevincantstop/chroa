using System;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ChroaVideoPlayer.ViewModels;

namespace ChroaVideoPlayer.Controls;

public partial class MpvVideoPlayer : UserControl
{
    public VideoPlayerViewModel ViewModel { get; } = new();

    public event Action? FullScreenToggleRequested;

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

    public void Play(string path)
    {
        ViewModel.Mpv.ViewContext = MpvViewControl.MpvContext;

        ViewModel.Play(path);
    }

    private void OnBarDragStarted() => ViewModel.IsDragging = true;

    private void OnBarDragCommitted(double fraction) => ViewModel.IsDragging = false;

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e) => ApplyState();

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

    private void OnPlayPauseClick(object? sender, RoutedEventArgs e) => ViewModel.Mpv.TogglePause();

    private void OnMuteClick(object? sender, RoutedEventArgs e) => ViewModel.Mpv.ToggleMute();

    private void OnFullScreenClick(object? sender, RoutedEventArgs e) => FullScreenToggleRequested?.Invoke();
}
