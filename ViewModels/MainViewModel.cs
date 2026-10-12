using System.ComponentModel;
using ChroaVideoPlayer.Mpv;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ChroaVideoPlayer.ViewModels
{
    public class MainViewModel : ObservableObject
    {
        public VideoPlayerViewModel Player { get; }

        public MainViewModel(VideoPlayerViewModel player)
        {
            Player = player;
            Player.PropertyChanged += OnPlayerPropertyChanged;
        }

        public ChroaMpvContext Mpv => Player.Mpv;

        public bool ShowLoadVideo => !Player.HasVideo || Player.HasEnded;

        private void OnPlayerPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(VideoPlayerViewModel.HasVideo) or nameof(VideoPlayerViewModel.HasEnded))
            {
                OnPropertyChanged(nameof(ShowLoadVideo));
            }
        }
    }
}