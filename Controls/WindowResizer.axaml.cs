using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Input;

namespace ChroaVideoPlayer.Controls;

public partial class WindowResizer : UserControl
{
    private readonly Dictionary<object, WindowEdge> _edges;

    public WindowResizer()
    {
        InitializeComponent();

        _edges = new Dictionary<object, WindowEdge>
        {
            [TopLeft] = WindowEdge.NorthWest,
            [TopRight] = WindowEdge.NorthEast,
            [BottomLeft] = WindowEdge.SouthWest,
            [BottomRight] = WindowEdge.SouthEast,
        };
    }

    private void OnGripPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is null || !_edges.TryGetValue(sender, out var edge)) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        e.Handled = true;

        if (TopLevel.GetTopLevel(this) is not Window window) return;

        window.BeginResizeDrag(edge, e);
    }
}