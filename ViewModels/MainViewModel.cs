using System;
using Avalonia.Threading;
using ChroaVideoPlayer.Mpv;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ChroaVideoPlayer.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        private const string StartupFile = @"D:\Movies\Horror\Dune-IMAX.mkv";

        /// <summary>mpv 交互全部收敛在 ChroaMpvContext 里，这里只持有它。</summary>
        public ChroaMpvContext Mpv { get; } = new();

        /// <summary>视频宽高比（显示宽 / 显示高）。0 表示还没探测到。</summary>
        [ObservableProperty]
        public partial double VideoAspect { get; set; }

        public MainViewModel()
        {
            Mpv.VideoAspectDetected += OnVideoAspectDetected;

            // 启动播放：此刻视图还没创建、mpv 上下文还没推过来，
            // ChroaMpvContext 会记住文件并在上下文就绪后自动播放。
            Mpv.Play(StartupFile);
        }

        private void OnVideoAspectDetected(object? sender, double aspect) =>
            // mpv 事件在它自己的线程上，状态统一在 UI 线程上改，
            // 这样订阅方的 PropertyChanged 也一定在 UI 线程。
            Dispatcher.UIThread.Post(() => VideoAspect = aspect);
    }
}