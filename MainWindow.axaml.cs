using System;
using System.Collections.Generic;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using ChroaVideoPlayer.ViewModels;
using Avalonia.Platform.Storage;

namespace ChroaVideoPlayer;

public partial class MainWindow : Window
{
    /// <summary>四角缩放热区 → 缩放方向。</summary>
    private readonly Dictionary<object, WindowEdge> _gripEdges;

    private MainViewModel? _viewModel;

    public MainWindow()
    {
        InitializeComponent();

        _gripEdges = new Dictionary<object, WindowEdge>
        {
            [GripTopLeft] = WindowEdge.NorthWest,
            [GripTopRight] = WindowEdge.NorthEast,
            [GripBottomLeft] = WindowEdge.SouthWest,
            [GripBottomRight] = WindowEdge.SouthEast,
        };
    }

    // ==== 拖动窗口：整窗任意位置（按钮与四角热区除外）====

    private void OnRootPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Handled) return;                                          // 按钮 / 四角热区已经处理掉
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        // 两种情况不要抢：
        //   1) 源元素是四角热区（保险，正常路径会被上面的 e.Handled 挡掉）
        //   2) 源元素有 Button 祖先 —— 包括点在按钮内部的 Path 上
        if (e.Source is Visual source &&
            (_gripEdges.ContainsKey(source) || source.FindAncestorOfType<Button>(true) is not null))
        {
            return;
        }

        BeginMoveDrag(e);
    }

    // ==== 四角缩放 ====

    private void OnResizeGripPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is null || !_gripEdges.TryGetValue(sender, out var edge)) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        e.Handled = true;       // 不要让根上的"拖动窗口"再处理一次
        BeginResizeDrag(edge, e);
    }

    // ==== 双击控制带空白处 = 最大化 / 还原 ====
    // 刻意仍挂在 Chrome 而不是 Root：双击视频区域要留给以后的"双击全屏"（架构文档 §5.4）
    private void OnChromeDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (!ReferenceEquals(e.Source, sender)) return;
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    // ==== 窗口自动适应视频宽高比（只读 ViewModel 的状态，不直接碰 mpv）====

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        // DataContext 可能被换掉，先把旧的解绑
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = DataContext as MainViewModel;

        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // VideoAspect 只在 UI 线程上被赋值（见 MainViewModel），所以这里不需要再切线程
        if (e.PropertyName == nameof(MainViewModel.VideoAspect) && _viewModel is not null)
        {
            ApplyVideoAspect(_viewModel.VideoAspect);
        }
    }

    private void ApplyVideoAspect(double aspect)
    {
        if (aspect <= 0 || double.IsNaN(aspect) || double.IsInfinity(aspect)) return;

        // 最大化 / 全屏时不调整
        if (WindowState != WindowState.Normal) return;

        // 宽度保持不变，按视频比例推高度
        var width = Width;
        var height = width / aspect;

        // 限制在当前屏幕的工作区内。
        // Screen.WorkingArea 是物理像素，而 Window.Width/Height 是 DIP，所以要除以显示缩放。
        var screen = Screens.ScreenFromWindow(this);
        if (screen is null)
        {
            screen = Screens.Primary;
        }

        if (screen is not null)
        {
            var scaling = RenderScaling > 0 ? RenderScaling : 1.0;
            var maxWidth = screen.WorkingArea.Width / scaling;
            var maxHeight = screen.WorkingArea.Height / scaling;

            var scale = Math.Min(1.0, Math.Min(maxWidth / width, maxHeight / height));
            width *= scale;
            height *= scale;
        }

        Width = Math.Round(width);
        Height = Math.Round(height);
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        // 拖动过程中给光标反馈：有文件 → 复制，其它内容 → 禁止
        e.DragEffects = HasDroppedFile(e) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        e.Handled = true;

        var path = GetFirstDroppedFilePath(e);
        if (path is null) return;

        // 复用现有链路：ChroaMpvContext 会等渲染上下文就绪后再 loadfile，
        // 并且新文件加载后会自动重新按宽高比调整窗口。
        _viewModel?.Mpv.Play(path);
    }

    private static bool HasDroppedFile(DragEventArgs e)
    {
        // 注意 Avalonia 12 是 DataTransfer，不是 Data（IDataObject）
        var files = e.DataTransfer.TryGetFiles();
        return files is { Length: > 0 };
    }

    private static string? GetFirstDroppedFilePath(DragEventArgs e)
    {
        var files = e.DataTransfer.TryGetFiles();
        if (files is null) return null;

        foreach (var item in files)
        {
            // 只接受文件；拖进来的是文件夹就忽略（文件夹播放是另一件事，现在不做）
            if (item is not IStorageFile) continue;

            var path = item.TryGetLocalPath();
            if (!string.IsNullOrEmpty(path)) return path;
        }

        return null;
    }

    // ==== 三个窗口按钮 ====

    private void OnMinimizeClick(object? sender, RoutedEventArgs e) =>
        WindowState = WindowState.Minimized;

    private void OnMaximizeClick(object? sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    private void OnPlayPauseClick(object? sender, RoutedEventArgs e) => _viewModel?.Mpv.TogglePause();
}