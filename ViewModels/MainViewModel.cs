using System;
using Avalonia.Threading;
using ChroaVideoPlayer.Mpv;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ChroaVideoPlayer.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        /// <summary>mpv 交互全部收敛在 ChroaMpvContext 里，这里只持有它。</summary>
        public ChroaMpvContext Mpv { get; } = new();

        /// <summary>视频宽高比（显示宽 / 显示高）。0 表示还没探测到。</summary>
        [ObservableProperty]
        public partial double VideoAspect { get; set; }

        /// <summary>是否处于暂停状态。由 mpv 的 pause 属性推送而来，UI 用它切换按钮图标。</summary>
        [ObservableProperty]
        public partial bool IsPaused { get; set; }

        /// <summary>
        /// 是否已经加载了媒体（对应 mpv 的 file-loaded）。
        /// UI 用它决定：空状态显示"Load Video"入口，还是显示播放/暂停控制条。
        /// 注意不能拿 IsPaused 当判据 —— mpv 空载时也报 pause=false，语义上根本没有"暂停"这回事。
        /// </summary>
        [ObservableProperty]
        public partial bool HasVideo { get; set; }

        /// <summary>当前播放位置（秒）。播放中由 mpv 推来；拖动进度条时是拖动值。</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(PositionText))]
        [NotifyPropertyChangedFor(nameof(SeekFraction))]
        public partial double Position { get; set; }

        /// <summary>总时长（秒）。0 表示还不知道（还没解析完 / 直播流）。</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(DurationText))]
        [NotifyPropertyChangedFor(nameof(DurationMaximum))]
        [NotifyPropertyChangedFor(nameof(CanSeek))]
        [NotifyPropertyChangedFor(nameof(SeekFraction))]
        public partial double Duration { get; set; }

        /// <summary>进度条的量程。Duration 还是 0 时给 1，免得视图算比例时除零。</summary>
        public double DurationMaximum => Duration > 0 ? Duration : 1;

        /// <summary>时长未知就不能 seek（比如直播流），视图会直接忽略拖动。</summary>
        public bool CanSeek => Duration > 0;

        /// <summary>进度条的填充比例（0~1）。视图把它交给 DragBar，不用自己算宽度。</summary>
        public double SeekFraction => Math.Clamp(Position / DurationMaximum, 0, 1);

        /// <summary>当前位置的显示文本（mm:ss / h:mm:ss）。</summary>
        public string PositionText => Format(Position);

        /// <summary>总时长的显示文本。</summary>
        public string DurationText => Format(Duration);

        /// <summary>用户正在拖动进度条。此时 mpv 推来的位置要让位给手指，否则两者会打架。</summary>
        [ObservableProperty]
        public partial bool IsSeeking { get; set; }

        /// <summary>音量上限。mpv 的 volume 可以到 volume-max（默认 130），这个 UI 只做到 100。</summary>
        public const double MaxVolume = 100;

        /// <summary>音量（0~100，mpv 的 volume 单位）。初值取 mpv 的默认音量，真实值由 ChroaMpvContext 推来。</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(VolumeFraction))]
        public partial double Volume { get; set; }

        /// <summary>是否静音。点音量图标切换。</summary>
        [ObservableProperty]
        public partial bool IsMuted { get; set; }

        /// <summary>音量条的填充比例（0~1）。</summary>
        public double VolumeFraction => Math.Clamp(Volume / MaxVolume, 0, 1);

        public MainViewModel()
        {
            Mpv.VideoAspectDetected += OnVideoAspectDetected;
            Mpv.PauseChanged += OnPauseChanged;
            Mpv.MediaOpened += OnMediaOpened;
            Mpv.ProgressChanged += OnProgressChanged;
            Mpv.VolumeChanged += OnVolumeChanged;

            // mpv 的默认音量是 100；真实值推来之前先按默认显示，免得音量条空着
            Volume = MaxVolume;
        }

        private void OnVideoAspectDetected(object? sender, double aspect) =>
            // mpv 事件在它自己的线程上，状态统一在 UI 线程上改，
            // 这样订阅方的 PropertyChanged 也一定在 UI 线程。
            Dispatcher.UIThread.Post(() => VideoAspect = aspect);

        private void OnPauseChanged(object? sender, bool paused) =>
            Dispatcher.UIThread.Post(() => IsPaused = paused);

        private void OnMediaOpened(object? sender, EventArgs e) =>
            // 和上面两个一样：mpv 线程 → UI 线程
            Dispatcher.UIThread.Post(() => HasVideo = true);

        // ==== 进度条拖动：视图只给"指针在轨道上的横向比例"，换算和状态都在这里 ====

        /// <summary>开始拖动：此后到 CommitSeek 之间，mpv 推来的位置一律忽略。</summary>
        public void BeginSeek() => IsSeeking = true;

        /// <summary>拖动中：把比例（0~1）换算成秒，只改本地显示，不碰 mpv。</summary>
        public void UpdateSeekFraction(double fraction)
        {
            if (!IsSeeking) return;

            // 用 Duration 而不是 DurationMaximum：能拖就说明 CanSeek 为真，Duration 一定 > 0
            Position = Math.Clamp(fraction, 0, 1) * Duration;
        }

        /// <summary>松手：把位置交给 mpv。此刻 Position 就是用户选定的位置。</summary>
        public void CommitSeek()
        {
            if (!IsSeeking) return;

            var target = Position;

            // 先解锁再 seek：SeekTo 会立刻乐观上报一次位置，
            // 若 IsSeeking 还是 true，这次上报会被"拖动中忽略"逻辑吞掉，进度条就会停在拖动值上。
            IsSeeking = false;
            Mpv.SeekTo(target);
        }

        private void OnProgressChanged(object? sender, PlaybackProgress progress) =>
            Dispatcher.UIThread.Post(() =>
            {
                if (IsSeeking) return;      // 拖动中：mpv 的回报让位给手指

                Duration = progress.Duration;
                Position = progress.Position;
            });

        /// <summary>秒 → "mm:ss"；超过一小时用 "h:mm:ss"。</summary>
        private static string Format(double seconds)
        {
            if (!double.IsFinite(seconds) || seconds < 0) seconds = 0;

            var t = TimeSpan.FromSeconds(seconds);
            return t.TotalHours >= 1
                ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}"
                : $"{t.Minutes:00}:{t.Seconds:00}";
        }

        // ==== 音量条 ====

        /// <summary>
        /// 音量条拖动中：比例 → 0~100，**立刻**写给 mpv（音量要边拖边听得到，
        /// 所以不像进度条那样等松手才生效）。
        /// </summary>
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
    }
}
