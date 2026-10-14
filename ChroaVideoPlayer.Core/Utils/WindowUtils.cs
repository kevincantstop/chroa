using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;

namespace ChroaVideoPlayer.Core.Utils;

public static class WindowUtils
{
    private const int DwmwaBorderColor = 34;
    private const int DwmwaColorNone = unchecked((int)0xFFFFFFFE);
    private const int FrameSlackPixels = 8;

    [DllImport("dwmapi.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    public static void RemoveSystemBorder(Window window)
    {
        if (!OperatingSystem.IsWindows()) return;

        var handle = window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (handle == IntPtr.Zero)
        {
            Debug.WriteLine("Could not get the window handle; the system border was not removed");
            return;
        }

        var none = DwmwaColorNone;
        try
        {
            var hr = DwmSetWindowAttribute(handle, DwmwaBorderColor, ref none, sizeof(int));
            if (hr != 0) Debug.WriteLine($"DWMWA_BORDER_COLOR failed: 0x{hr:X8} (unsupported before Windows 11 build 22000; safe to ignore)");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"DWMWA_BORDER_COLOR threw: {ex.Message}");
        }
    }

    public static void ClampToWorkingArea(Window window, double fraction) =>
        FitSize(window, window.Width, window.Height, fraction);

    public static bool FitToAspect(Window window, double aspect, double fraction)
    {
        if (aspect <= 0 || double.IsNaN(aspect) || double.IsInfinity(aspect)) return false;

        if (window.WindowState != WindowState.Normal) return false;

        var width = window.Width;

        return FitSize(window, width, width / aspect, fraction);
    }

    public static bool FitInsideWorkingArea(Window window, double width, double height) =>
        FitSize(window, width, height, 1.0);

    private static bool FitSize(Window window, double width, double height, double fraction)
    {
        if (width <= 0 || height <= 0) return false;

        if (GetWorkingAreaSize(window) is not { } area) return false;

        var scale = Math.Min(1.0, Math.Min(area.Width * fraction / width, area.Height * fraction / height));

        ApplySize(window, width * scale, height * scale);

        return true;
    }

    private static void ApplySize(Window window, double width, double height)
    {
        window.Width = Math.Round(width);
        window.Height = Math.Round(height);

        KeepInsideWorkingArea(window);
    }

    private static Size? GetWorkingAreaSize(Window window)
    {
        var screen = window.Screens.ScreenFromWindow(window) ?? window.Screens.Primary;
        if (screen is null) return null;

        var scaling = screen.Scaling > 0 ? screen.Scaling : 1.0;
        return new Size(screen.WorkingArea.Width / scaling, screen.WorkingArea.Height / scaling);
    }

    private static void KeepInsideWorkingArea(Window window)
    {
        var screen = window.Screens.ScreenFromWindow(window) ?? window.Screens.Primary;
        if (screen is null) return;

        var scaling = screen.Scaling > 0 ? screen.Scaling : 1.0;
        var work = screen.WorkingArea;
        var pixelWidth = (int)Math.Round(window.Width * scaling) + FrameSlackPixels;
        var pixelHeight = (int)Math.Round(window.Height * scaling) + FrameSlackPixels;
        var pos = window.Position;

        var x = Math.Clamp(pos.X, work.X, Math.Max(work.X, work.Right - pixelWidth));
        var y = Math.Clamp(pos.Y, work.Y, Math.Max(work.Y, work.Bottom - pixelHeight));
        if (x != pos.X || y != pos.Y) window.Position = new PixelPoint(x, y);
    }
}
