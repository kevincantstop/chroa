using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Shapes;
using Avalonia.Data;
using Avalonia.Media;
using Avalonia.Platform;
using ChroaVideoPlayer.Utils;
using HAlign = Avalonia.Layout.HorizontalAlignment;
using VAlign = Avalonia.Layout.VerticalAlignment;

using Path = Avalonia.Controls.Shapes.Path;

namespace ChroaVideoPlayer.Controls;

public class SvgIcon : Decorator
{
    public static readonly StyledProperty<string?> IconProperty =
        AvaloniaProperty.Register<SvgIcon, string?>(nameof(Icon));

    public static readonly StyledProperty<double> IconSizeProperty =
        AvaloniaProperty.Register<SvgIcon, double>(nameof(IconSize), 16d);

    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        TextElement.ForegroundProperty.AddOwner<SvgIcon>();

    private static readonly Dictionary<string, Geometry?> IconCache = new();

    private readonly Viewbox _viewbox;
    private readonly Path _path;

    public SvgIcon()
    {
        _path = new Path
        {
            StrokeThickness = 2,
            StrokeLineCap = PenLineCap.Round,
            StrokeJoin = PenLineJoin.Round,
        };
        _path.Bind(Shape.StrokeProperty, new Binding(nameof(Foreground)) { Source = this });

        _viewbox = new Viewbox
        {
            Width = IconSize,
            Height = IconSize,
            HorizontalAlignment = HAlign.Center,
            VerticalAlignment = VAlign.Center,
            Child = new Canvas
            {
                Width = 24,
                Height = 24,
                Children = { _path },
            },
        };

        Child = _viewbox;

        IsHitTestVisible = false;
    }

    public string? Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public double IconSize
    {
        get => GetValue(IconSizeProperty);
        set => SetValue(IconSizeProperty, value);
    }

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == IconProperty)
        {
            _path.Data = ResolveIcon(Icon);
        }
        else if (change.Property == IconSizeProperty)
        {
            _viewbox.Width = _viewbox.Height = IconSize;
        }
    }

    private static Geometry? ResolveIcon(string? source)
    {
        if (string.IsNullOrWhiteSpace(source)) return null;

        if (!source.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                return Geometry.Parse(source);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to parse icon path data: {ex.Message}");
                return null;
            }
        }

        if (IconCache.TryGetValue(source, out var cached)) return cached;

        Geometry? geometry = null;
        try
        {
            var uri = source.StartsWith("avares://", StringComparison.OrdinalIgnoreCase)
                ? new Uri(source)
                : new Uri($"avares://{typeof(SvgIcon).Assembly.GetName().Name}/{source.TrimStart('/')}");

            using var stream = AssetLoader.Open(uri, null);
            using var reader = new StreamReader(stream);
            geometry = SvgGeometryLoader.Parse(reader.ReadToEnd());
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to load icon file {source}: {ex.Message}");
        }

        IconCache[source] = geometry;
        return geometry;
    }
}
