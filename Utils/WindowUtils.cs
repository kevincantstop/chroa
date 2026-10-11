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

    // 这个声明只管"取值为 int/DWORD 的 DWM attribute"：pvAttribute 是 ref int + 固定 4 字节。
    // 将来若要用取值为结构体或其它宽度的 attribute，必须另写一个重载，别拿这个凑。
    //
    // DefaultDllImportSearchPaths：dwmapi.dll 是系统库，限定只从 System32 解析，
    // 堵掉"exe 目录放个同名 DLL 抢先加载"的经典劫持面。
    // 另外 P/Invoke 的原生库是首次调用时才加载的，所以这个声明在非 Windows 平台上放着也没事
    // （而且 RemoveSystemBorder 一开头就返回了）。
    [DllImport("dwmapi.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    /// <summary>
    /// 把系统画的那圈 1px 窗口边框设成"无"，投影与圆角保留
    /// （<c>WindowDecorations="BorderOnly"</c> 让 Windows 重新给窗口投影，代价就是这圈线）。
    /// 最佳努力：非 Windows、拿不到句柄、或系统早于 Windows 11 Build 22000（那个 attribute 还不认识）
    /// 时静默返回 —— 最后一种情况只是边框还在，既不崩也不抛。
    /// </summary>
    public static void RemoveSystemBorder(Window window)
    {
        if (!OperatingSystem.IsWindows()) return;

        // 必须在窗口真正创建之后调用（见 MainWindow.OnOpened），此时 HWND 一定存在
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
