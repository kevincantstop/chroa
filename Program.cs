using Avalonia;
using HanumanInstitute.LibMpv.Core;
using System;
using System.IO;
using System.Runtime.InteropServices;

namespace ChroaVideoPlayer;

class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        // LibMpv searches MpvApi.RootPath only, and resolves libmpv-2.dll lazily the moment the first
        // MpvContext is built (MpvView creates it while the window's XAML loads). The DLL ships in the
        // native\ subfolder rather than the exe folder, so point RootPath there first. Assigning this
        // plain static path neither loads the native library nor touches Avalonia.
        MpvApi.RootPath = Path.Combine(AppContext.BaseDirectory, "native");

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
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
