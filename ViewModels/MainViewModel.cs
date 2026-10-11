using System;
using Avalonia.Threading;
using ChroaVideoPlayer.Mpv;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ChroaVideoPlayer.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        public ChroaMpvContext Mpv { get; } = new();

        [ObservableProperty]
        public partial double VideoAspect { get; set; }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ShowPlayIcon))]
        [NotifyPropertyChangedFor(nameof(ShowPauseIcon))]
        public partial bool IsPaused { get; set; }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ShowPlayIcon))]
        [NotifyPropertyChangedFor(nameof(ShowPauseIcon))]
        [NotifyPropertyChangedFor(nameof(CanSeek))]
        [NotifyPropertyChangedFor(nameof(ShowLoadVideo))]
        public partial bool HasEnded { get; set; }

        public bool ShowPlayIcon => IsPaused || HasEnded;

        public bool ShowPauseIcon => !ShowPlayIcon;

        public bool ShowLoadVideo => !HasVideo || HasEnded;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ShowLoadVideo))]
        public partial bool HasVideo { get; set; }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(PositionText))]
        [NotifyPropertyChangedFor(nameof(SeekFraction))]
        public partial double Position { get; set; }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(DurationText))]
        [NotifyPropertyChangedFor(nameof(DurationMaximum))]
        [NotifyPropertyChangedFor(nameof(CanSeek))]
        [NotifyPropertyChangedFor(nameof(SeekFraction))]
        public partial double Duration { get; set; }

        public double DurationMaximum => Duration > 0 ? Duration : 1;

        public bool CanSeek => Duration > 0 && !HasEnded;

        public double SeekFraction => Math.Clamp(Position / DurationMaximum, 0, 1);

        public string PositionText => Format(Position);

        public string DurationText => Duration > 0 ? Format(Duration) : "--:--";

        [ObservableProperty]
        public partial bool IsSeeking { get; set; }

        public const double MaxVolume = 100;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(VolumeFraction))]
        public partial double Volume { get; set; }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ShowMutedIcon))]
        [NotifyPropertyChangedFor(nameof(ShowVolumeIcon))]
        public partial bool IsMuted { get; set; }

        public double VolumeFraction => Math.Clamp(Volume / MaxVolume, 0, 1);

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanAdjustVolume))]
        [NotifyPropertyChangedFor(nameof(ShowMutedIcon))]
        [NotifyPropertyChangedFor(nameof(ShowVolumeIcon))]
        public partial bool HasAudioTrack { get; set; }

        public bool CanAdjustVolume => HasAudioTrack;

        public bool ShowMutedIcon => IsMuted || !HasAudioTrack;

        public bool ShowVolumeIcon => !ShowMutedIcon;

        public MainViewModel()
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

        private void OnVideoAspectDetected(object? sender, double aspect) =>
            Dispatcher.UIThread.Post(() => VideoAspect = aspect);

        private void OnPauseChanged(object? sender, bool paused) =>
            Dispatcher.UIThread.Post(() => IsPaused = paused);

        private void OnMediaOpened(object? sender, EventArgs e) =>
            Dispatcher.UIThread.Post(() => HasVideo = true);


        public void BeginSeek() => IsSeeking = true;

        public void UpdateSeekFraction(double fraction)
        {
            if (!IsSeeking) return;

            Position = Math.Clamp(fraction, 0, 1) * Duration;
        }

        public void CommitSeek()
        {
            if (!IsSeeking) return;

            var target = Position;

            IsSeeking = false;
            Mpv.SeekTo(target);
        }

        private void OnProgressChanged(object? sender, PlaybackProgress progress) =>
            Dispatcher.UIThread.Post(() =>
            {
                if (IsSeeking) return;

                Duration = progress.Duration;
                Position = progress.Position;
            });

        private static string Format(double seconds)
        {
            if (!double.IsFinite(seconds) || seconds < 0) seconds = 0;

            var t = TimeSpan.FromSeconds(seconds);
            return t.TotalHours >= 1
                ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}"
                : $"{t.Minutes:00}:{t.Seconds:00}";
        }


        public void UpdateVolumeFraction(double fraction)
        {
            Volume = Math.Clamp(fraction, 0, 1) * MaxVolume;
            Mpv.SetVolume(Volume);
        }

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
