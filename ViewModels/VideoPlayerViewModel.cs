using System;
using Avalonia.Threading;
using ChroaVideoPlayer.Mpv;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ChroaVideoPlayer.ViewModels
{
    public partial class VideoPlayerViewModel : ObservableObject
    {
        public ChroaMpvContext Mpv { get; } = new();

        public const double MaxVolume = 100;

        [ObservableProperty]
        public partial double VideoAspect { get; set; }

        [ObservableProperty]
        public partial bool IsPaused { get; set; }

        [ObservableProperty]
        public partial bool HasEnded { get; set; }

        [ObservableProperty]
        public partial bool HasVideo { get; set; }

        [ObservableProperty]
        public partial double Position { get; set; }

        [ObservableProperty]
        public partial double Duration { get; set; }

        [ObservableProperty]
        public partial bool IsSeeking { get; set; }

        [ObservableProperty]
        public partial bool IsDragging { get; set; }

        [ObservableProperty]
        public partial double Volume { get; set; }

        [ObservableProperty]
        public partial bool IsMuted { get; set; }

        [ObservableProperty]
        public partial bool HasAudioTrack { get; set; }

        public VideoPlayerViewModel()
        {
            Mpv.VideoAspectDetected += OnVideoAspectDetected;
            Mpv.PauseChanged += OnPauseChanged;
            Mpv.MediaOpened += OnMediaOpened;
            Mpv.ProgressChanged += OnProgressChanged;
            Mpv.VolumeChanged += OnVolumeChanged;
            Mpv.EndedChanged += OnEndedChanged;
            Mpv.AudioTrackChanged += OnAudioTrackChanged;

            Volume = MaxVolume;

            HasAudioTrack = true;
        }

        public void Play(string path) => Mpv.Play(path);


        public void BeginSeek() => IsSeeking = true;

        public void UpdatePosition(double seconds)
        {
            if (!IsSeeking) return;

            Position = Math.Clamp(seconds, 0, Duration);
        }

        public void CommitSeek()
        {
            if (!IsSeeking) return;

            var target = Position;

            IsSeeking = false;
            Mpv.SeekTo(target);
        }

        public void SetVolume(double volume)
        {
            Volume = Math.Clamp(volume, 0, MaxVolume);
            Mpv.SetVolume(Volume);
        }


        private void OnVideoAspectDetected(object? sender, double aspect) =>
            Dispatcher.UIThread.Post(() => VideoAspect = aspect);

        private void OnPauseChanged(object? sender, bool paused) =>
            Dispatcher.UIThread.Post(() => IsPaused = paused);

        private void OnMediaOpened(object? sender, EventArgs e) =>
            Dispatcher.UIThread.Post(() => HasVideo = true);

        private void OnProgressChanged(object? sender, PlaybackProgress progress) =>
            Dispatcher.UIThread.Post(() =>
            {
                if (IsSeeking) return;

                Duration = progress.Duration;
                Position = progress.Position;
            });

        private void OnVolumeChanged(object? sender, PlaybackVolume volume) =>
            Dispatcher.UIThread.Post(() =>
            {
                Volume = volume.Volume;
                IsMuted = volume.Muted;
            });

        private void OnEndedChanged(object? sender, bool ended) =>
            Dispatcher.UIThread.Post(() => HasEnded = ended);

        private void OnAudioTrackChanged(object? sender, bool hasAudio) =>
            Dispatcher.UIThread.Post(() => HasAudioTrack = hasAudio);
    }
}
