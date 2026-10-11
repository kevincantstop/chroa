using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia.Controls;

namespace ChroaVideoPlayer.Utils;

/// <summary>
/// 窗口相关的平台小工具。目前只有一件事：去掉 DWM 画在窗口四周的那圈 1px 系统边框。
/// </summary>
internal static class WindowUtils
{
    // DWMWA_BORDER_COLOR（Windows 11 Build 22000 起支持）与它的特殊值：
    // DWMWA_COLOR_NONE = 0xFFFFFFFE 表示"不画边框" —— 用它只去边框，投影与圆角保留。
    private const int DwmwaBorderColor = 34;
    private const int DwmwaColorNone = unchecked((int)0xFFFFFFFE);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    /// <summary>
    /// 把系统画的那圈 1px 窗口边框设成"无"，投影与圆角保留
    /// （<c>WindowDecorations="BorderOnly"</c> 让 Windows 重新给窗口投影，代价就是这圈线）。
    /// 最佳努力：非 Windows、拿不到句柄、或系统早于 Windows 11 Build 22000 时静默返回。
    /// </summary>
    public static void RemoveSystemBorder(Window window)
    {
        if (!OperatingSystem.IsWindows()) return;

        // 必须在窗口真正创建之后调用（见 MainWindow.OnOpened），此时 HWND 一定存在
        var handle = window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (handle == IntPtr.Zero)
        {
            Debug.WriteLine("拿不到窗口句柄，系统边框没去掉");
            return;
        }

        var none = DwmwaColorNone;
        try
        {
            var hr = DwmSetWindowAttribute(handle, DwmwaBorderColor, ref none, sizeof(int));
            if (hr != 0) Debug.WriteLine($"DWMWA_BORDER_COLOR 失败: 0x{hr:X8}（Win11 22000 以下不支持，可忽略）");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"DWMWA_BORDER_COLOR 抛异常: {ex.Message}");
        }
    }
}
