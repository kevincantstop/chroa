using Avalonia;
using HanumanInstitute.LibMpv.Core;
using System;
using System.IO;
using System.Runtime.InteropServices;

namespace ChroaVideoPlayer;

class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        MpvApi.RootPath = Path.Combine(AppContext.BaseDirectory, "native");

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        AppBuilder builder = AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont();

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            builder.With(new Win32PlatformOptions
            {
                RenderingMode = [Win32RenderingMode.AngleEgl, Win32RenderingMode.Software]
            });
        } 
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            builder.With(new AvaloniaNativePlatformOptions
            {
                RenderingMode = [AvaloniaNativeRenderingMode.OpenGl, AvaloniaNativeRenderingMode.Software]
            });
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            builder.With(new X11PlatformOptions
            {
                RenderingMode = [X11RenderingMode.Egl, X11RenderingMode.Glx, X11RenderingMode.Software]
            });
        }  

        return builder.LogToTrace();
    }
}
