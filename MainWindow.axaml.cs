using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using ChroaVideoPlayer.Controls;
using ChroaVideoPlayer.Utils;
using ChroaVideoPlayer.ViewModels;
using Avalonia.Platform.Storage;

namespace ChroaVideoPlayer;

public partial class MainWindow : Window
{
    /// <summary>四角缩放热区 → 缩放方向。</summary>
    private readonly Dictionary<object, WindowEdge> _gripEdges;

    private MainViewModel? _viewModel;

    /// <summary>进度条与音量条。落在它们上面的指针归它们自己，窗口不抢。</summary>
    private readonly DragBar _seekBar;
    private readonly DragBar _volumeBar;

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

        // 两条比例条共用同一套拖动逻辑（Controls/DragBar.cs），差别只在"比例代表什么"。
        _seekBar = new DragBar(SeekArea, SeekTrack, SeekFill)
        {
            CanDrag = () => _viewModel?.CanSeek == true,     // 时长未知（直播流）就不给拖
        };
        _seekBar.Started += () => _viewModel?.BeginSeek();
        _seekBar.Dragged += fraction => _viewModel?.UpdateSeekFraction(fraction);
        _seekBar.Committed += _ => _viewModel?.CommitSeek();

        _volumeBar = new DragBar(VolumeArea, VolumeTrack, VolumeFill)
        {
            CanDrag = () => _viewModel is { HasVideo: true, CanAdjustVolume: true },
        };
        _volumeBar.Dragged += fraction => _viewModel?.UpdateVolumeFraction(fraction);
        // 音量不需要收尾动作（拖动中就已经写给 mpv 了），所以不接 Committed

        // 拖动期间让浮层保持显示（指针被捕获后可能被拖到窗口外）
        KeepChromeVisibleWhileDragging(_seekBar);
        KeepChromeVisibleWhileDragging(_volumeBar);
    }

    // ==== 窗口外观（平台相关）====

    /// <summary>
    /// 自动定尺寸的上限：最多占屏幕工作区的这个比例。
    /// 留边距的原因：窗口一旦顶满屏幕，顶部那排自绘按钮（最小化 / 最大化 / 关闭）
    /// 就有被挤出屏幕的风险 —— 想铺满请走最大化，不要靠"开窗就顶满"。
    /// 只作用于本类主动改尺寸的那两条路径，用户自己拖 resize / 最大化一概不受影响。
    /// </summary>
    private const double MaxWorkingAreaFraction = 0.8;

    /// <summary>夹位置时给窗口边框留的物理像素余量（BorderOnly 那条不可见的缩放边）。</summary>
    private const int FrameSlackPixels = 8;

    // 必须在窗口真正创建之后调用，所以挂在 OnOpened，而不是构造函数里。
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        // 去掉 DWM 画的那圈 1px 系统边框（阴影与圆角保留），细节见 Utils/WindowUtils.cs
        WindowUtils.RemoveSystemBorder(this);

        // 默认尺寸也走同一条上限（只缩不放）。大屏上 1280x720 本来就够小，这段不会触发，
        // 是给小屏 / 高缩放的机器兜底的。
        ClampToMaxWorkingArea();
    }

    /// <summary>
    /// 当前屏幕允许的最大窗口尺寸（DIP），已按 <see cref="MaxWorkingAreaFraction"/> 折算。
    /// WorkingArea 是物理像素、Window.Width/Height 是 DIP，所以用同一块屏自己的 Scaling 换算。
    /// 拿不到屏幕信息时返回 null —— 调用方据此放弃自动定尺寸。
    /// </summary>
    private (double Width, double Height)? GetMaxWindowSize()
    {
        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (screen is null) return null;

        var scaling = screen.Scaling > 0 ? screen.Scaling : 1.0;
        return (screen.WorkingArea.Width / scaling * MaxWorkingAreaFraction,
                screen.WorkingArea.Height / scaling * MaxWorkingAreaFraction);
    }

    /// <summary>
    /// 自动定尺寸的统一出口：写回窗口，并保证整个窗口落在工作区内。
    /// 位置要一起管 —— 尺寸算对了、但窗口本来就贴着屏幕右 / 下边缘时，
    /// 右上角那排按钮（尤其关闭）会被推到屏幕外面，正是"看不到、点不到"的样子。
    /// 只在本类主动改尺寸时调用，用户拖动 resize / 最大化一概不碰。
    /// </summary>
    private void ApplyWindowSize(double width, double height)
    {
        Width = Math.Round(width);
        Height = Math.Round(height);

        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (screen is null) return;

        var scaling = screen.Scaling > 0 ? screen.Scaling : 1.0;
        var work = screen.WorkingArea;                                  // 物理像素
        var pixelWidth = (int)Math.Round(Width * scaling) + FrameSlackPixels;
        var pixelHeight = (int)Math.Round(Height * scaling) + FrameSlackPixels;
        var pos = Position;

        // 只把越界的那部分推回来；本来就在范围内的方向不动
        var x = Math.Clamp(pos.X, work.X, Math.Max(work.X, work.Right - pixelWidth));
        var y = Math.Clamp(pos.Y, work.Y, Math.Max(work.Y, work.Bottom - pixelHeight));
        if (x != pos.X || y != pos.Y) Position = new PixelPoint(x, y);
    }

    /// <summary>
    /// 把当前尺寸按同一上限等比缩小（只缩不放，比例不变）。
    /// XAML 里写死的 1280x720 在小屏 / 高缩放的机器上本身就可能超过 80%。
    /// </summary>
    private void ClampToMaxWorkingArea()
    {
        if (GetMaxWindowSize() is not { } max) return;

        var scale = Math.Min(1.0, Math.Min(max.Width / Width, max.Height / Height));
        if (scale >= 1.0) return;

        ApplyWindowSize(Width * scale, Height * scale);
    }

    // ==== 拖动窗口：整窗任意位置（按钮与四角热区除外）====

    private void OnRootPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Handled) return;                                          // 按钮 / 四角热区已经处理掉
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        // 四种情况不要抢：
        //   1) 源元素是四角热区（保险，正常路径会被上面的 e.Handled 挡掉）
        //   2) 源元素有 Button 祖先 —— 包括点在按钮内部的 Path 上
        //   3) 源元素在进度条 / 音量条里 —— 那根指针归它们自己
        if (e.Source is Visual source &&
            (_gripEdges.ContainsKey(source)
             || source.FindAncestorOfType<Button>(true) is not null
             || IsInside(source, _seekBar.Area)
             || IsInside(source, _volumeBar.Area)))
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

            // 订阅到之前 ViewModel 就已经有值了（它的默认值），而 [ObservableProperty] 生成的
            // setter 只在"值真的变了"时才 raise PropertyChanged —— 光靠通知会让视图停在初值上。
            // 所以这里必须主动拉一次：音量条曾因此永远显示 0（mpv 侧其实是 100，声音也在响）。
            _seekBar.SetFraction(_viewModel.SeekFraction);
            _volumeBar.SetFraction(_viewModel.VolumeFraction);
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
        else if (e.PropertyName == nameof(MainViewModel.SeekFraction))
        {
            _seekBar.SetFraction(_viewModel.SeekFraction);
        }
        else if (e.PropertyName == nameof(MainViewModel.VolumeFraction))
        {
            _volumeBar.SetFraction(_viewModel.VolumeFraction);
        }
    }

    private void ApplyVideoAspect(double aspect)
    {
        if (aspect <= 0 || double.IsNaN(aspect) || double.IsInfinity(aspect)) return;

        // 最大化 / 全屏时不调整
        if (WindowState != WindowState.Normal) return;

        // 拿不到屏幕信息就没有上限可用 —— 此时宁可不按比例调整，
        // 也绝不能算出一个比屏幕还大的窗口（旧实现这个分支是"完全不夹"的）。
        if (GetMaxWindowSize() is not { } max) return;

        // 宽度保持不变，按视频比例推高度；超出上限就整体等比缩小 —— 比例始终等于视频比例
        var width = Width;
        var height = width / aspect;
        var scale = Math.Min(1.0, Math.Min(max.Width / width, max.Height / height));

        ApplyWindowSize(width * scale, height * scale);
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

    // ==== 进度条 / 音量条 ====
    // 指针交互（捕获、命中、已填充宽度）都在 Controls/DragBar.cs 里，两条共用；
    // 这里只把"比例"翻译成各自的含义：进度 → 秒（走 ViewModel 的 seek 三件套），音量 → 0~100。
    // 两条的接线在构造函数末尾。

    /// <summary>
    /// 拖动比例条期间给根 Grid 挂一个 dragging 类，让浮层保持显示 —— 指针被捕获后可能被拖到
    /// 窗口外，那时 :pointerover 失效，控制条会在拖动中途自己淡掉（样式见 Styles/WindowChrome.axaml）。
    /// </summary>
    private void KeepChromeVisibleWhileDragging(DragBar bar)
    {
        bar.Started += () => RootGrid.Classes.Set("dragging", true);
        bar.Committed += _ => RootGrid.Classes.Set("dragging", false);
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

    private void OnMuteClick(object? sender, RoutedEventArgs e) => _viewModel?.Mpv.ToggleMute();
}