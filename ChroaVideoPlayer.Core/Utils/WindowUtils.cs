using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;

namespace ChroaVideoPlayer.Core.Utils;

public static class WindowUtils
{
    public const double PanelShadowMargin = 24;

    private const int DwmwaUseImmersiveDarkMode = 20;
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

        TrySetAttribute(handle, DwmwaUseImmersiveDarkMode, 1, nameof(DwmwaUseImmersiveDarkMode));
        TrySetAttribute(handle, DwmwaBorderColor, DwmwaColorNone, nameof(DwmwaBorderColor));
    }

    private static void TrySetAttribute(IntPtr handle, int attribute, int value, string name)
    {
        try
        {
            var hr = DwmSetWindowAttribute(handle, attribute, ref value, sizeof(int));
            if (hr != 0) Debug.WriteLine($"{name} failed: 0x{hr:X8} (unsupported on this Windows build; safe to ignore)");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"{name} threw: {ex.Message}");
        }
    }

    public static void ClampToWorkingArea(Window window, double fraction) =>
        FitSize(window, window.Width, window.Height, fraction);

    public static bool FitToAspect(Window window, double aspect, double fraction, double margin = 0)
    {
        if (aspect <= 0 || double.IsNaN(aspect) || double.IsInfinity(aspect)) return false;

        if (window.WindowState != WindowState.Normal) return false;

        var width = Math.Max(1, window.Width - margin * 2);

        return FitSize(window, width + margin * 2, width / aspect + margin * 2, fraction);
    }

    public static bool FitInsideWorkingArea(Window window, double width, double height) =>
        FitSize(window, width, height, 1.0);

    public static PixelRect GetClientBounds(Window window)
    {
        var scaling = window.RenderScaling > 0 ? window.RenderScaling : 1;
        var size = window.ClientSize;

        return new PixelRect(window.Position, new PixelSize(
            Math.Max(1, (int)Math.Round(size.Width * scaling)),
            Math.Max(1, (int)Math.Round(size.Height * scaling))));
    }

    public static PixelRect GetPanelBounds(Window window)
    {
        var bounds = GetClientBounds(window);
        var scaling = window.RenderScaling > 0 ? window.RenderScaling : 1;
        var inset = Math.Max(0, (int)Math.Round(PanelShadowMargin * scaling));

        return new PixelRect(
            bounds.X + inset,
            bounds.Y + inset,
            Math.Max(1, bounds.Width - inset * 2),
            Math.Max(1, bounds.Height - inset * 2));
    }

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

    public static void KeepInsideWorkingArea(Window window, Window? anchor = null)
    {
        var screen = (anchor is not null ? anchor.Screens.ScreenFromWindow(anchor) : null)
            ?? window.Screens.ScreenFromWindow(window)
            ?? window.Screens.Primary;
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
