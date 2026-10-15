using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace ChroaVideoPlayer.Core.Plugin.UI;

public sealed class PluginWindowResizer
{
    private static readonly Cursor LeftCursor = new(StandardCursorType.LeftSide);

    private static readonly Cursor RightCursor = new(StandardCursorType.RightSide);

    private static readonly Cursor TopCursor = new(StandardCursorType.TopSide);

    private static readonly Cursor BottomCursor = new(StandardCursorType.BottomSide);

    private static readonly Cursor TopLeftCursor = new(StandardCursorType.TopLeftCorner);

    private static readonly Cursor TopRightCursor = new(StandardCursorType.TopRightCorner);

    private static readonly Cursor BottomLeftCursor = new(StandardCursorType.BottomLeftCorner);

    private static readonly Cursor BottomRightCursor = new(StandardCursorType.BottomRightCorner);

    private readonly Window _window;

    private readonly InputElement _surface;

    private IPointer? _pointer;

    private Point _startPointer;

    private Size _startSize;

    private PixelPoint _startPosition;

    private PixelPoint _appliedPosition;

    private double _scaling = 1;

    private ResizeEdge _edge;

    public PluginWindowResizer(Window window, InputElement surface)
    {
        _window = window;
        _surface = surface;

        _surface.PointerMoved += OnPointerMoved;
        _surface.PointerReleased += OnPointerReleased;
        _surface.PointerCaptureLost += OnPointerCaptureLost;
        _surface.PointerExited += OnPointerExited;
    }

    public event Action? Ended;

    public bool IsEnabled { get; set; } = true;

    public double MinimumSide { get; set; } = 120;

    public double Band { get; set; } = 6;

    public Func<PixelSize, PixelPoint?>? DockedPosition { get; set; }

    public bool IsResizing => _pointer is not null;

    public bool TryStart(PointerPressedEventArgs e)
    {
        if (!IsEnabled) return false;
        if (_pointer is not null) return false;
        if (_window.WindowState != WindowState.Normal) return false;
        if (!e.GetCurrentPoint(_window).Properties.IsLeftButtonPressed) return false;

        var edge = EdgeAt(e.GetPosition(_surface));

        if (edge == ResizeEdge.None) return false;

        e.Handled = true;

        _scaling = _window.RenderScaling > 0 ? _window.RenderScaling : 1;
        _startPointer = e.GetPosition(_window);
        _startSize = _window.ClientSize;
        _startPosition = _window.Position;
        _appliedPosition = _startPosition;
        _edge = edge;
        _pointer = e.Pointer;

        _surface.Cursor = CursorFor(edge);

        e.Pointer.Capture(_surface);

        return true;
    }

    public void End()
    {
        if (_pointer is null) return;

        _pointer = null;
        _edge = ResizeEdge.None;

        Ended?.Invoke();
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_pointer is not null)
        {
            if (!ReferenceEquals(e.Pointer, _pointer)) return;

            e.Handled = true;

            Apply(e);

            return;
        }

        if (!IsEnabled) return;

        _surface.Cursor = CursorFor(EdgeAt(e.GetPosition(_surface)));
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_pointer is null || !ReferenceEquals(e.Pointer, _pointer)) return;

        e.Handled = true;

        End();

        e.Pointer.Capture(null);
    }

    private void OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e) => End();

    private void OnPointerExited(object? sender, PointerEventArgs e)
    {
        if (_pointer is not null) return;

        _surface.Cursor = null;
    }

    private void Apply(PointerEventArgs e)
    {
        var current = e.GetPosition(_window);

        var movedX = current.X - _startPointer.X + ((_appliedPosition.X - _startPosition.X) / _scaling);
        var movedY = current.Y - _startPointer.Y + ((_appliedPosition.Y - _startPosition.Y) / _scaling);

        var left = _edge is ResizeEdge.Left or ResizeEdge.TopLeft or ResizeEdge.BottomLeft;
        var right = _edge is ResizeEdge.Right or ResizeEdge.TopRight or ResizeEdge.BottomRight;
        var top = _edge is ResizeEdge.Top or ResizeEdge.TopLeft or ResizeEdge.TopRight;
        var bottom = _edge is ResizeEdge.Bottom or ResizeEdge.BottomLeft or ResizeEdge.BottomRight;

        var width = left || right ? Math.Max(Math.Round(_startSize.Width + (left ? -movedX : movedX)), MinimumSide) : _startSize.Width;
        var height = top || bottom ? Math.Max(Math.Round(_startSize.Height + (top ? -movedY : movedY)), MinimumSide) : _startSize.Height;

        _window.Width = width;
        _window.Height = height;

        if (DockedPosition?.Invoke(new PixelSize(
                (int)Math.Round(width * _scaling),
                (int)Math.Round(height * _scaling))) is { } docked)
        {
            _window.Position = docked;
            _appliedPosition = docked;

            return;
        }

        if (!left && !top) return;

        var anchorX = left ? _startPosition.X + (int)Math.Round(_startSize.Width * _scaling) : _startPosition.X;
        var anchorY = top ? _startPosition.Y + (int)Math.Round(_startSize.Height * _scaling) : _startPosition.Y;

        _appliedPosition = new PixelPoint(
            left ? anchorX - (int)Math.Round(width * _scaling) : anchorX,
            top ? anchorY - (int)Math.Round(height * _scaling) : anchorY);

        _window.Position = _appliedPosition;
    }

    private ResizeEdge EdgeAt(Point point)
    {
        var bounds = _surface.Bounds;

        if (bounds.Width <= Band * 2 || bounds.Height <= Band * 2) return ResizeEdge.None;

        var left = point.X <= Band;
        var right = point.X >= bounds.Width - Band;
        var top = point.Y <= Band;
        var bottom = point.Y >= bounds.Height - Band;

        if (left && top) return ResizeEdge.TopLeft;
        if (right && top) return ResizeEdge.TopRight;
        if (left && bottom) return ResizeEdge.BottomLeft;
        if (right && bottom) return ResizeEdge.BottomRight;
        if (left) return ResizeEdge.Left;
        if (right) return ResizeEdge.Right;
        if (top) return ResizeEdge.Top;
        if (bottom) return ResizeEdge.Bottom;

        return ResizeEdge.None;
    }

    private static Cursor? CursorFor(ResizeEdge edge) => edge switch
    {
        ResizeEdge.Left => LeftCursor,
        ResizeEdge.Right => RightCursor,
        ResizeEdge.Top => TopCursor,
        ResizeEdge.Bottom => BottomCursor,
        ResizeEdge.TopLeft => TopLeftCursor,
        ResizeEdge.TopRight => TopRightCursor,
        ResizeEdge.BottomLeft => BottomLeftCursor,
        ResizeEdge.BottomRight => BottomRightCursor,
        _ => null,
    };

    private enum ResizeEdge
    {
        None,
        Left,
        Right,
        Top,
        Bottom,
        TopLeft,
        TopRight,
        BottomLeft,
        BottomRight,
    }
}
