using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace ChroaVideoPlayer.Controls;

public partial class WindowResizer : UserControl
{
    public static readonly StyledProperty<double> AspectProperty =
        AvaloniaProperty.Register<WindowResizer, double>(nameof(Aspect));

    private const double MinimumSide = 120;

    private const double AxisThreshold = 3;

    private readonly Dictionary<object, ResizeCorner> _corners;

    private IPointer? _pointer;

    private Window? _window;

    private ResizeCorner _corner;

    private Point _startPointer;

    private Size _startSize;

    private PixelPoint _startPosition;

    private PixelPoint _appliedPosition;

    private double _scaling = 1;

    private bool? _widthDrives;

    public WindowResizer()
    {
        InitializeComponent();

        _corners = new Dictionary<object, ResizeCorner>
        {
            [TopLeft] = ResizeCorner.TopLeft,
            [TopRight] = ResizeCorner.TopRight,
            [BottomLeft] = ResizeCorner.BottomLeft,
            [BottomRight] = ResizeCorner.BottomRight,
        };
    }

    public double Aspect
    {
        get => GetValue(AspectProperty);
        set => SetValue(AspectProperty, value);
    }

    private void OnGripPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not IInputElement grip) return;
        if (!_corners.TryGetValue(grip, out var corner)) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        e.Handled = true;

        if (TopLevel.GetTopLevel(this) is not Window window) return;
        if (window.WindowState != WindowState.Normal) return;

        _pointer = e.Pointer;
        _window = window;
        _corner = corner;
        _startPointer = e.GetPosition(window);
        _startSize = window.ClientSize;
        _startPosition = window.Position;
        _appliedPosition = _startPosition;
        _scaling = window.RenderScaling > 0 ? window.RenderScaling : 1;
        _widthDrives = null;

        e.Pointer.Capture(grip);
    }

    private void OnGripMoved(object? sender, PointerEventArgs e)
    {
        if (_window is null || !ReferenceEquals(e.Pointer, _pointer)) return;

        e.Handled = true;

        var current = e.GetPosition(_window);

        var movedX = current.X - _startPointer.X + ((_appliedPosition.X - _startPosition.X) / _scaling);
        var movedY = current.Y - _startPointer.Y + ((_appliedPosition.Y - _startPosition.Y) / _scaling);

        if (_widthDrives is null && (Math.Abs(movedX) >= AxisThreshold || Math.Abs(movedY) >= AxisThreshold))
        {
            _widthDrives = Math.Abs(movedX) >= Math.Abs(movedY);
        }

        var left = _corner is ResizeCorner.TopLeft or ResizeCorner.BottomLeft;
        var top = _corner is ResizeCorner.TopLeft or ResizeCorner.TopRight;

        var width = _startSize.Width + (left ? -movedX : movedX);
        var height = _startSize.Height + (top ? -movedY : movedY);

        var target = Constrain(width, height);

        _window.Width = target.Width;
        _window.Height = target.Height;

        if (!left && !top) return;

        var anchorX = left ? _startPosition.X + (int)Math.Round(_startSize.Width * _scaling) : _startPosition.X;
        var anchorY = top ? _startPosition.Y + (int)Math.Round(_startSize.Height * _scaling) : _startPosition.Y;

        var pixelWidth = (int)Math.Round(target.Width * _scaling);
        var pixelHeight = (int)Math.Round(target.Height * _scaling);

        _appliedPosition = new PixelPoint(left ? anchorX - pixelWidth : anchorX, top ? anchorY - pixelHeight : anchorY);

        _window.Position = _appliedPosition;
    }

    private void OnGripReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!ReferenceEquals(e.Pointer, _pointer)) return;

        e.Handled = true;

        EndDrag();

        e.Pointer.Capture(null);
    }

    private void OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e) => EndDrag();

    private void EndDrag()
    {
        _pointer = null;
        _window = null;
        _widthDrives = null;
    }

    private Size Constrain(double width, double height)
    {
        var aspect = Aspect;

        if (aspect <= 0 || double.IsNaN(aspect) || double.IsInfinity(aspect))
        {
            return new Size(Math.Max(Math.Round(width), MinimumSide), Math.Max(Math.Round(height), MinimumSide));
        }

        if (_widthDrives == false)
        {
            var drivenHeight = Math.Max(Math.Round(height), MinimumSide);

            return new Size(Math.Round(drivenHeight * aspect), drivenHeight);
        }

        var drivenWidth = Math.Max(Math.Round(width), MinimumSide);

        return new Size(drivenWidth, Math.Round(drivenWidth / aspect));
    }

    private enum ResizeCorner
    {
        TopLeft,
        TopRight,
        BottomLeft,
        BottomRight,
    }
}
