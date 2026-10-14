using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia.Controls;

namespace ChroaVideoPlayer.Utils;

public static class WindowUtils
{
    private const int DwmwaBorderColor = 34;
    private const int DwmwaColorNone = unchecked((int)0xFFFFFFFE);

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
}
