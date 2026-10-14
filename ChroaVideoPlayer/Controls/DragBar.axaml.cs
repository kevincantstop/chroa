using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using System;

namespace ChroaVideoPlayer.Controls;

public partial class DragBar : UserControl
{
    private IPointer? _pointer;
    private double _fraction;

    public event Action? Started;

    public event Action<double>? Dragged;

    public event Action<double>? Committed;

    public Func<bool>? CanDrag { get; set; }

    public DragBar()
    {
        InitializeComponent();

        Area.PointerPressed += OnPointerPressed;
        Area.PointerMoved += OnPointerMoved;
        Area.PointerReleased += OnPointerReleased;
        Area.PointerCaptureLost += OnPointerCaptureLost;

        Track.SizeChanged += (_, _) => ApplyFill();
    }

    public void SetFraction(double fraction)
    {
        _fraction = Math.Clamp(fraction, 0, 1);
        ApplyFill();
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (CanDrag is not null && !CanDrag()) return;
        if (!e.GetCurrentPoint(Area).Properties.IsLeftButtonPressed) return;

        e.Handled = true;
        _pointer = e.Pointer;
        e.Pointer.Capture(Area);

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
        var width = Area.Bounds.Width;
        if (width <= 0) return;

        SetFraction(e.GetPosition(Area).X / width);
        Dragged?.Invoke(_fraction);
    }

    private void ApplyFill()
    {
        var trackWidth = Track.Bounds.Width;
        Fill.Width = trackWidth <= 0 ? 0 : Math.Round(_fraction * trackWidth);
    }
}