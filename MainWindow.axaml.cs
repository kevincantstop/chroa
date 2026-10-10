using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ChroaVideoPlayer.Diagnostics;
using HanumanInstitute.LibMpv;

namespace ChroaVideoPlayer;

public partial class MainWindow : Window
{
    private DispatcherTimer? _dumpTimer;
    private int _dumpTicks;

    public MainWindow()
    {
        InitializeComponent();
    }

    // ==== 临时诊断：定位"图标不居中 / 指针只在图标附近变手型" ====
    // ==== 定位完连同 Diagnostics/LayoutDump.cs 一起删掉 ====
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        // 立刻 dump 一次（即使尺寸还是 0，也先证明这条写文件路径是通的）
        DumpNow();

        // 之后每 500ms 再 dump 一次、共 10 次，覆盖布局稳定后的真实状态。
        // 不依赖 LayoutUpdated —— 首次布局在 Show() 期间就跑完了，OnOpened 之后
        // 它可能再也不触发，这正是上一版什么都没写出来的原因。
        _dumpTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _dumpTimer.Tick += (_, _) =>
        {
            DumpNow();
            if (++_dumpTicks >= 10)
            {
                _dumpTimer?.Stop();
                _dumpTimer = null;
            }
        };
        _dumpTimer.Start();
    }

    private void DumpNow() =>
        LayoutDump.Dump(this, Path.Combine(AppContext.BaseDirectory, "layout-dump.txt"));
    // ==== 诊断结束 ====

    private void OnMinimizeClick(object? sender, RoutedEventArgs e) =>
        WindowState = WindowState.Minimized;

    private void OnMaximizeClick(object? sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}