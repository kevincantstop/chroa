using System;
using Avalonia.Controls;
using Avalonia.Input;

namespace ChroaVideoPlayer.Controls;

/// <summary>
/// 把"命中区 Border + 轨道 Border + 已填充 Border"这一组变成一个可拖动的比例条
/// —— 进度条和音量条共用它，所以指针交互只写这一份。
///
/// 分工：
///   · 命中与拖动：area 负责（它必须有 Background，null 背景不参与命中测试）
///   · 已填充宽度：本类按"比例 × 轨道实宽"算，轨道尺寸变化时会自动重算
///   · 含义：本类只认 0~1 的比例 —— 换算成秒 / 音量是调用方的事
/// </summary>
internal sealed class DragBar
{
    private readonly Border _area;
    private readonly Border _track;
    private readonly Border _fill;

    private IPointer? _pointer;
    private double _fraction;

    /// <summary>按下时触发（在第一次 Dragged 之前）。</summary>
    public event Action? Started;

    /// <summary>拖动中，每次指针移动都触发。参数是 0~1 的比例。</summary>
    public event Action<double>? Dragged;

    /// <summary>松手 / 指针捕获丢失时触发，用来收尾。参数是最终比例。</summary>
    public event Action<double>? Committed;

    /// <summary>返回 false 时按下会被忽略（比如时长未知时的进度条）。默认恒为 true。</summary>
    public Func<bool>? CanDrag { get; set; }

    public DragBar(Border area, Border track, Border fill)
    {
        _area = area;
        _track = track;
        _fill = fill;

        _area.PointerPressed += OnPointerPressed;
        _area.PointerMoved += OnPointerMoved;
        _area.PointerReleased += OnPointerReleased;
        _area.PointerCaptureLost += OnPointerCaptureLost;

        // 轨道宽度变了（窗口缩放）→ 已填充段的宽度要重算
        _track.SizeChanged += (_, _) => ApplyFill();
    }

    /// <summary>命中区。窗口的"拖动窗口"守卫要用它放行落在条上的指针。</summary>
    public Control Area => _area;

    /// <summary>外部状态变化时刷新已填充宽度（比如 mpv 推来新的播放进度 / 音量）。</summary>
    public void SetFraction(double fraction)
    {
        _fraction = Math.Clamp(fraction, 0, 1);
        ApplyFill();
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (CanDrag is not null && !CanDrag()) return;
        if (!e.GetCurrentPoint(_area).Properties.IsLeftButtonPressed) return;

        e.Handled = true;                   // 保险：别让根上的"拖动窗口"接走
        _pointer = e.Pointer;
        e.Pointer.Capture(_area);           // 指针移出这 20px 也要继续收到 Moved

        Started?.Invoke();
        UpdateFromPointer(e);
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!ReferenceEquals(e.Pointer, _pointer)) return;

        e.Handled = true;
        UpdateFromPointer(e);
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!ReferenceEquals(e.Pointer, _pointer)) return;

        e.Handled = true;
        _pointer = null;
        e.Pointer.Capture(null);

        UpdateFromPointer(e);               // 松手那一点也算进去
        Committed?.Invoke(_fraction);
    }

    private void OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        // 捕获丢了（窗口失活、指针被别处抢走）：必须收尾，
        // 否则订阅方（比如 ViewModel 的 IsSeeking）会一直停在"拖动中"。
        if (_pointer is null) return;

        _pointer = null;
        Committed?.Invoke(_fraction);
    }

    private void UpdateFromPointer(PointerEventArgs e)
    {
        var width = _area.Bounds.Width;
        if (width <= 0) return;

        SetFraction(e.GetPosition(_area).X / width);
        Dragged?.Invoke(_fraction);
    }

    private void ApplyFill()
    {
        var trackWidth = _track.Bounds.Width;
        _fill.Width = trackWidth <= 0 ? 0 : Math.Round(_fraction * trackWidth);
    }
}
