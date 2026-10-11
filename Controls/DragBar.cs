using System;
using Avalonia.Controls;
using Avalonia.Input;

namespace ChroaVideoPlayer.Controls;

internal sealed class DragBar
{
    private readonly Border _area;
    private readonly Border _track;
    private readonly Border _fill;

    private IPointer? _pointer;
    private double _fraction;

    public event Action? Started;

    public event Action<double>? Dragged;

    public event Action<double>? Committed;

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

        _track.SizeChanged += (_, _) => ApplyFill();
    }

    public Control Area => _area;

    public void SetFraction(double fraction)
    {
        _fraction = Math.Clamp(fraction, 0, 1);
        ApplyFill();
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (CanDrag is not null && !CanDrag()) return;
        if (!e.GetCurrentPoint(_area).Properties.IsLeftButtonPressed) return;

        e.Handled = true;
        _pointer = e.Pointer;
        e.Pointer.Capture(_area);

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

        UpdateFromPointer(e);
        Committed?.Invoke(_fraction);
    }

    private void OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
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
