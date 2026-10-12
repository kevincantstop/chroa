using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ChroaVideoPlayer.ViewModels;

namespace ChroaVideoPlayer.Controls;

public partial class MpvVideoPlayer : UserControl
{
    public VideoPlayerViewModel ViewModel { get; } = new();

    public MpvVideoPlayer()
    {
        DataContext = ViewModel;

        InitializeComponent();

        SeekBar.CanDrag = () => ViewModel.CanSeek;
        SeekBar.Started += () => ViewModel.BeginSeek();
        SeekBar.Dragged += fraction => ViewModel.UpdateSeekFraction(fraction);
        SeekBar.Committed += _ => ViewModel.CommitSeek();

        VolumeBar.CanDrag = () => ViewModel.HasVideo && ViewModel.CanAdjustVolume;
        VolumeBar.Dragged += fraction => ViewModel.UpdateVolumeFraction(fraction);

        SeekBar.Started += OnBarDragStarted;
        SeekBar.Committed += OnBarDragCommitted;
        VolumeBar.Started += OnBarDragStarted;
        VolumeBar.Committed += OnBarDragCommitted;

        SeekBar.SetFraction(ViewModel.SeekFraction);
        VolumeBar.SetFraction(ViewModel.VolumeFraction);

        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    public void Play(string path)
    {
        ViewModel.Mpv.ViewContext = MpvViewControl.MpvContext;

        ViewModel.Play(path);
    }

    private void OnBarDragStarted() => ViewModel.IsDragging = true;

    private void OnBarDragCommitted(double fraction) => ViewModel.IsDragging = false;

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(VideoPlayerViewModel.SeekFraction))
        {
            SeekBar.SetFraction(ViewModel.SeekFraction);
        }
        else if (e.PropertyName == nameof(VideoPlayerViewModel.VolumeFraction))
        {
            VolumeBar.SetFraction(ViewModel.VolumeFraction);
        }
    }

    private void OnPlayPauseClick(object? sender, RoutedEventArgs e) => ViewModel.Mpv.TogglePause();

    private void OnMuteClick(object? sender, RoutedEventArgs e) => ViewModel.Mpv.ToggleMute();
}