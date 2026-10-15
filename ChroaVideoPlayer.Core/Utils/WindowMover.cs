using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace ChroaVideoPlayer.Core.Utils;

public sealed class WindowMover
{
    private readonly Window _window;

    private readonly InputElement _surface;

    private IPointer? _pointer;

    private Point _startPointer;

    private PixelPoint _startPosition;

    private PixelPoint _appliedPosition;

    private double _scaling = 1;

    public WindowMover(Window window, InputElement surface)
    {
        _window = window;
        _surface = surface;

        _surface.PointerMoved += OnPointerMoved;
        _surface.PointerReleased += OnPointerReleased;
        _surface.PointerCaptureLost += OnPointerCaptureLost;
    }

    public event Action? Started;

    public event Action? Ended;

    public Func<PixelPoint, PixelPoint>? Adjust { get; set; }

    public bool IsMoving => _pointer is not null;

    public bool TryStart(PointerPressedEventArgs e)
    {
        if (_pointer is not null) return false;
        if (!e.GetCurrentPoint(_window).Properties.IsLeftButtonPressed) return false;
        if (_window.WindowState != WindowState.Normal) return false;

        e.Handled = true;

        _scaling = _window.RenderScaling > 0 ? _window.RenderScaling : 1;
        _startPointer = e.GetPosition(_window);
        _startPosition = _window.Position;
        _appliedPosition = _startPosition;
        _pointer = e.Pointer;

        e.Pointer.Capture(_surface);

        Started?.Invoke();

        return true;
    }

    public void End()
    {
        if (_pointer is null) return;

        _pointer = null;

        Ended?.Invoke();
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!ReferenceEquals(e.Pointer, _pointer)) return;

        e.Handled = true;

        var current = e.GetPosition(_window);

        var movedX = current.X - _startPointer.X + ((_appliedPosition.X - _startPosition.X) / _scaling);
        var movedY = current.Y - _startPointer.Y + ((_appliedPosition.Y - _startPosition.Y) / _scaling);

        var proposed = new PixelPoint(
            _startPosition.X + (int)Math.Round(movedX * _scaling),
            _startPosition.Y + (int)Math.Round(movedY * _scaling));

        if (Adjust is not null) proposed = Adjust(proposed);

        _appliedPosition = proposed;

        _window.Position = proposed;
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!ReferenceEquals(e.Pointer, _pointer)) return;

        e.Handled = true;

        End();

        e.Pointer.Capture(null);
    }

    private void OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e) => End();
}
