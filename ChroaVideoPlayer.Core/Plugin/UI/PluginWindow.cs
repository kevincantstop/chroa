using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using ChroaVideoPlayer.Core.Utils;

namespace ChroaVideoPlayer.Core.Plugin.UI;

public class PluginWindow : Window
{
    public static readonly StyledProperty<DockSide> InitialDockSideProperty =
        AvaloniaProperty.Register<PluginWindow, DockSide>(nameof(InitialDockSide));

    private const double MinimumVisible = 24;

    private const double ShadowMargin = 24;

    private readonly Border _panel;

    private readonly WindowMover _mover;

    private Window? _host;

    private DockSide _side;

    private int _offset;

    private bool _hiddenByHost;

    private bool _opened;

    public PluginWindow()
    {
        WindowDecorations = WindowDecorations.None;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        Background = Brushes.Transparent;
        CanResize = false;
        CanMinimize = false;
        CanMaximize = false;
        ShowInTaskbar = false;

        _panel = new Border
        {
            Margin = new Thickness(ShadowMargin),
            Background = Brushes.Black,
            BoxShadow = BoxShadows.Parse("0 6 24 0 #A0000000"),
            CornerRadius = new CornerRadius(10),
        };

        Content = _panel;

        _mover = new WindowMover(this, _panel) { Adjust = AdjustPosition };

        _mover.Ended += OnMoveEnded;

        _panel.PointerPressed += OnPointerPressed;
    }

    public Control? PluginContent
    {
        get => _panel.Child;
        set => _panel.Child = value;
    }

    public double SnapDistance { get; set; } = 12;

    public double DetachDistance { get; set; } = 24;

    public DockSide InitialDockSide
    {
        get => GetValue(InitialDockSideProperty);
        set => SetValue(InitialDockSideProperty, value);
    }

    public double InitialDockOffset { get; set; }

    public Window? Host
    {
        get => _host;
        set => SetHost(value);
    }

    public DockSide AttachedSide => _side;

    public bool IsAttached => _side != DockSide.None;

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        if (_host is null && Owner is Window owner) SetHost(owner);

        if (_opened) return;

        _opened = true;

        ApplyInitialDock();
    }

    protected override void OnClosed(EventArgs e)
    {
        SetHost(null);

        base.OnClosed(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property != ContentProperty) return;
        if (change.NewValue is not Control content) return;
        if (ReferenceEquals(content, _panel)) return;

        Content = _panel;

        _panel.Child = content;
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Handled) return;
        if (e.Source is Visual source && source.FindAncestorOfType<Button>(true) is not null) return;

        _mover.TryStart(e);
    }

    private void OnMoveEnded()
    {
        if (_side != DockSide.None)
        {
            SyncToHost();

            return;
        }

        WindowUtils.KeepInsideWorkingArea(this);
    }

    private PixelPoint AdjustPosition(PixelPoint proposed)
    {
        if (_host is not { } host) return proposed;

        var hostBounds = WindowUtils.GetClientBounds(host);
        var size = PanelSize;
        var bounds = PanelBounds(proposed, size);

        if (_side == DockSide.None)
        {
            var side = FindSide(bounds, hostBounds, SnapDistance * Scale, out var offset);

            if (side == DockSide.None) return proposed;

            _side = side;
            _offset = offset;
        }
        else if (Math.Abs(PerpendicularGap(_side, bounds, hostBounds)) > DetachDistance * Scale)
        {
            _side = DockSide.None;

            return proposed;
        }
        else
        {
            _offset = AlongOffset(_side, bounds, hostBounds, size, VisiblePixels);
        }

        return ToWindowPosition(DockedPosition(_side, hostBounds, size, _offset));
    }

    private void SyncToHost()
    {
        if (_side == DockSide.None || _host is not { } host) return;
        if (_mover.IsMoving) return;

        var size = PanelSize;

        Position = ToWindowPosition(DockedPosition(_side, WindowUtils.GetClientBounds(host), size, _offset));
    }

    private void ApplyInitialDock()
    {
        if (_host is not { } host) return;
        if (InitialDockSide == DockSide.None) return;

        var hostBounds = WindowUtils.GetClientBounds(host);
        var size = PanelSize;
        var requested = DockedPosition(InitialDockSide, hostBounds, size, (int)Math.Round(InitialDockOffset * Scale));

        _side = InitialDockSide;
        _offset = AlongOffset(_side, new PixelRect(requested, size), hostBounds, size, VisiblePixels);

        SyncToHost();
    }

    private PixelRect PanelBounds(PixelPoint windowPosition, PixelSize size) =>
        new(windowPosition.X + Inset, windowPosition.Y + Inset, size.Width, size.Height);

    private PixelPoint ToWindowPosition(PixelPoint panelPosition) =>
        new(panelPosition.X - Inset, panelPosition.Y - Inset);

    private PixelSize PanelSize
    {
        get
        {
            var size = WindowUtils.GetClientBounds(this).Size;
            var inset = Inset * 2;

            return new PixelSize(Math.Max(1, size.Width - inset), Math.Max(1, size.Height - inset));
        }
    }

    private int Inset => (int)Math.Round(ShadowMargin * Scale);

    private DockSide FindSide(PixelRect bounds, PixelRect host, double threshold, out int offset)
    {
        var side = DockSide.None;
        var best = threshold;
        var visible = VisiblePixels;

        if (OverlapsVertically(bounds, host, visible))
        {
            var right = Math.Abs(bounds.X - host.Right);
            var left = Math.Abs(host.X - bounds.Right);

            if (right < best)
            {
                best = right;

                side = DockSide.Right;
            }

            if (left < best)
            {
                best = left;

                side = DockSide.Left;
            }
        }

        if (OverlapsHorizontally(bounds, host, visible))
        {
            var bottom = Math.Abs(bounds.Y - host.Bottom);
            var top = Math.Abs(host.Y - bounds.Bottom);

            if (bottom < best)
            {
                best = bottom;

                side = DockSide.Bottom;
            }

            if (top < best)
            {
                best = top;

                side = DockSide.Top;
            }
        }

        offset = side == DockSide.None ? 0 : AlongOffset(side, bounds, host, bounds.Size, visible);

        return side;
    }

    private static bool OverlapsVertically(PixelRect bounds, PixelRect host, int visible) =>
        Math.Min(bounds.Bottom, host.Bottom) - Math.Max(bounds.Y, host.Y) >= visible;

    private static bool OverlapsHorizontally(PixelRect bounds, PixelRect host, int visible) =>
        Math.Min(bounds.Right, host.Right) - Math.Max(bounds.X, host.X) >= visible;

    private static int AlongOffset(DockSide side, PixelRect bounds, PixelRect host, PixelSize size, int visible)
    {
        var vertical = side is DockSide.Left or DockSide.Right;
        var start = vertical ? bounds.Y - host.Y : bounds.X - host.X;
        var extent = vertical ? host.Height : host.Width;
        var own = vertical ? size.Height : size.Width;
        var keep = Math.Min(visible, own);
        var lowest = -(own - keep);

        return (int)Math.Clamp(start, lowest, Math.Max(lowest, extent - keep));
    }

    private static int PerpendicularGap(DockSide side, PixelRect bounds, PixelRect host) => side switch
    {
        DockSide.Left => host.X - bounds.Right,
        DockSide.Right => bounds.X - host.Right,
        DockSide.Top => host.Y - bounds.Bottom,
        _ => bounds.Y - host.Bottom,
    };

    private static PixelPoint DockedPosition(DockSide side, PixelRect host, PixelSize size, int offset) => side switch
    {
        DockSide.Left => new PixelPoint(host.X - size.Width, host.Y + offset),
        DockSide.Right => new PixelPoint(host.Right, host.Y + offset),
        DockSide.Top => new PixelPoint(host.X + offset, host.Y - size.Height),
        _ => new PixelPoint(host.X + offset, host.Bottom),
    };

    private void SetHost(Window? host)
    {
        if (ReferenceEquals(_host, host)) return;

        if (_host is not null)
        {
            _host.PositionChanged -= OnHostPositionChanged;
            _host.Resized -= OnHostResized;
            _host.PropertyChanged -= OnHostPropertyChanged;
            _host.Closed -= OnHostClosed;
        }

        _host = host;
        _side = DockSide.None;
        _offset = 0;

        if (host is null) return;

        host.PositionChanged += OnHostPositionChanged;
        host.Resized += OnHostResized;
        host.PropertyChanged += OnHostPropertyChanged;
        host.Closed += OnHostClosed;

        Owner = host;
    }

    private void OnHostPositionChanged(object? sender, PixelPointEventArgs e) => SyncToHost();

    private void OnHostResized(object? sender, WindowResizedEventArgs e) => SyncToHost();

    private void OnHostPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != WindowStateProperty) return;
        if (_host is not { } host) return;
        if (host.WindowState == WindowState.Minimized) return;

        if (host.WindowState != WindowState.Normal && _side != DockSide.None)
        {
            _hiddenByHost = true;

            Hide();

            return;
        }

        if (_hiddenByHost)
        {
            _hiddenByHost = false;

            Show();
        }

        SyncToHost();
    }

    private void OnHostClosed(object? sender, EventArgs e) => Close();

    private double Scale => RenderScaling > 0 ? RenderScaling : 1;

    private int VisiblePixels => (int)Math.Round(MinimumVisible * Scale);
}
