using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
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

    /// <summary>正在拖动进度条的那根指针。null = 没在拖。</summary>
    private IPointer? _seekingPointer;

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

        // 三种情况不要抢：
        //   1) 源元素是四角热区（保险，正常路径会被上面的 e.Handled 挡掉）
        //   2) 源元素有 Button 祖先 —— 包括点在按钮内部的 Path 上
        //   3) 源元素在进度条拖动区里 —— 那根指针归进度条自己
        if (e.Source is Visual source &&
            (_gripEdges.ContainsKey(source)
             || source.FindAncestorOfType<Button>(true) is not null
             || IsInside(source, SeekArea)))
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
        if (_viewModel is null) return;

        // VideoAspect 只在 UI 线程上被赋值（见 MainViewModel），所以这里不需要再切线程
        if (e.PropertyName == nameof(MainViewModel.VideoAspect))
        {
            ApplyVideoAspect(_viewModel.VideoAspect);
        }
        else if (e.PropertyName is nameof(MainViewModel.Position) or nameof(MainViewModel.Duration))
        {
            // 位置 / 时长变了 → 已播段宽度要重算（拖动进度条时也走这里）
            UpdateSeekFill();
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

    // ==== 进度条：拖动 seek ====
    // 视图只负责"指针在轨道上的横向比例"，换算成秒、是否忽略 mpv 回报等都在 ViewModel 里。

    private void OnSeekPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(SeekArea).Properties.IsLeftButtonPressed) return;
        if (_viewModel is null || !_viewModel.CanSeek) return;    // 时长未知（比如直播流）就别拖

        e.Handled = true;               // 保险：别让根上的"拖动窗口"接走
        _seekingPointer = e.Pointer;
        e.Pointer.Capture(SeekArea);    // 指针移出这 20px 也要继续收到 Moved

        _viewModel.BeginSeek();
        UpdateSeekFromPointer(e);
    }

    private void OnSeekPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!ReferenceEquals(e.Pointer, _seekingPointer)) return;

        e.Handled = true;
        UpdateSeekFromPointer(e);
    }

    private void OnSeekPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!ReferenceEquals(e.Pointer, _seekingPointer)) return;

        e.Handled = true;
        _seekingPointer = null;
        e.Pointer.Capture(null);

        UpdateSeekFromPointer(e);       // 松手那一点也算进去
        _viewModel?.CommitSeek();
    }

    private void OnSeekPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        // 捕获丢了（窗口失活、指针被别处抢走）：必须收尾，否则 ViewModel 里的 IsSeeking
        // 会一直为真，从此忽略 mpv 的回报 —— 进度条就永远停住了。
        if (_seekingPointer is null) return;

        _seekingPointer = null;
        _viewModel?.CommitSeek();
    }

    private void UpdateSeekFromPointer(PointerEventArgs e)
    {
        var width = SeekArea.Bounds.Width;
        if (width <= 0) return;

        _viewModel?.UpdateSeekFraction(e.GetPosition(SeekArea).X / width);
    }

    private void OnSeekAreaSizeChanged(object? sender, SizeChangedEventArgs e) => UpdateSeekFill();

    /// <summary>
    /// 已播段的宽度 = 比例 × 轨道实际宽度。轨道还没布局完（宽度 0）就先按 0 处理。
    /// 之所以由代码算：3px 细线上没有可拖的 Thumb，宽度等于"比例 × 实宽"，绑定做不了乘法。
    /// </summary>
    private void UpdateSeekFill()
    {
        var trackWidth = SeekTrack.Bounds.Width;
        if (trackWidth <= 0 || _viewModel is null)
        {
            SeekFill.Width = 0;
            return;
        }

        var fraction = Math.Clamp(_viewModel.Position / _viewModel.DurationMaximum, 0, 1);
        SeekFill.Width = Math.Round(fraction * trackWidth);
    }

    /// <summary>source 自己或它的某个视觉祖先是不是 target。</summary>
    private static bool IsInside(Visual source, Visual target)
    {
        for (Visual? v = source; v is not null; v = v.GetVisualParent())
        {
            if (ReferenceEquals(v, target)) return true;
        }

        return false;
    }

    // ==== 空状态：选文件并播放 ====

    /// <summary>文件选择框的类型过滤器。mpv 能吃的格式远不止这些，所以第二个"所有文件"是逃生门。</summary>
    private static readonly FilePickerFileType VideoFileType = new("视频文件")
    {
        Patterns = new[]
        {
            "*.mp4", "*.mkv", "*.mov", "*.m4v", "*.avi", "*.webm", "*.flv",
            "*.wmv", "*.ts", "*.m2ts", "*.mpg", "*.mpeg", "*.rmvb", "*.3gp",
        },
        MimeTypes = new[] { "video/*" },
        AppleUniformTypeIdentifiers = new[] { "public.movie" },
    };

    private static readonly FilePickerFileType AllFileType = new("所有文件")
    {
        Patterns = new[] { "*.*" },
    };

    private async void OnLoadVideoClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            // StorageProvider 是 TopLevel 上的属性，Window 继承自 TopLevel，直接可用；
            // Windows 上它会把对话框挂到本窗口上（模态）。
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "选择视频",
                AllowMultiple = false,
                FileTypeFilter = [ VideoFileType, AllFileType ],
            });

            if (files.Count == 0) return;                 // 用户取消 → 空集合，不是 null

            var path = files[0].TryGetLocalPath();
            if (string.IsNullOrEmpty(path)) return;

            // 和拖放共用同一条链路：ChroaMpvContext 会等渲染上下文就绪后再 loadfile，
            // 加载成功后的 file-loaded 会把 HasVideo 打开 → 空状态按钮自动消失。
            // mpv 默认 pause=no，所以选完文件就是直接播放。
            _viewModel?.Mpv.Play(path);
        }
        catch (Exception ex)
        {
            // async void 事件处理器里的异常没人接，会直接掀掉进程 —— 必须自己兜住
            Debug.WriteLine($"Open file picker failed: {ex}");
        }
    }

    // ==== 三个窗口按钮 ====

    private void OnMinimizeClick(object? sender, RoutedEventArgs e) =>
        WindowState = WindowState.Minimized;

    private void OnMaximizeClick(object? sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    private void OnPlayPauseClick(object? sender, RoutedEventArgs e) => _viewModel?.Mpv.TogglePause();
}