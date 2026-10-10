using CommunityToolkit.Mvvm.ComponentModel;
using HanumanInstitute.LibMpv;
using HanumanInstitute.LibMpv.Avalonia;
using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace ChroaVideoPlayer.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        private const string StartupFile = @"D:\Movies\Horror\Dune-IMAX.mkv";

        [ObservableProperty]
        public partial MpvContext? Mpv { get; set; }
        
        [ObservableProperty]
        public partial VideoRenderer Renderer { get; set; }

        partial void OnMpvChanged(MpvContext? value)
        {
            if (value is null) return;
            _ = PlayStartupFileAsync(value);
        }

        private async Task PlayStartupFileAsync(MpvContext mpv)
        {
            try
            {
                var waited = 0;
                while (!mpv.IsCustomRendering() && waited < 5000)
                {
                    await Task.Delay(50);
                    waited += 50;
                }

                if (!mpv.IsCustomRendering())
                {
                    Debug.WriteLine("OpenGL 渲染上下文 5 秒内没建立，画面出不来（检查 ANGLE / RenderingMode）");
                }

                await mpv.LoadFile(StartupFile).InvokeAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Startup playback failed: {ex}");
            }
        }
    }
}
