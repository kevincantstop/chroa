using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using HAlign = Avalonia.Layout.HorizontalAlignment;
using VAlign = Avalonia.Layout.VerticalAlignment;

namespace ChroaVideoPlayer.Core.UI;

public enum SVGIconButtonShape
{
    Square,

    Circle,
}

public class SVGIconButton : Button
{
    public static readonly StyledProperty<string?> IconProperty =
        AvaloniaProperty.Register<SVGIconButton, string?>(nameof(Icon));

    public static readonly StyledProperty<double> IconSizeProperty =
        AvaloniaProperty.Register<SVGIconButton, double>(nameof(IconSize), 16d);

    public static readonly StyledProperty<double> ButtonSizeProperty =
        AvaloniaProperty.Register<SVGIconButton, double>(nameof(ButtonSize), 32d);

    public static readonly StyledProperty<SVGIconButtonShape> ButtonShapeProperty =
        AvaloniaProperty.Register<SVGIconButton, SVGIconButtonShape>(
            nameof(ButtonShape), SVGIconButtonShape.Circle);

    private readonly SvgIcon _icon;

    private readonly Border _hitArea;

    public SVGIconButton()
    {
        _icon = new SvgIcon();

        _hitArea = new Border
        {
            Background = Brushes.Transparent,
            HorizontalAlignment = HAlign.Stretch,
            VerticalAlignment = VAlign.Stretch,
            Child = _icon,
        };
        _hitArea.Bind(InputElement.CursorProperty, new Binding(nameof(Cursor)) { Source = this });

        Content = _hitArea;

        HorizontalContentAlignment = HAlign.Stretch;
        VerticalContentAlignment = VAlign.Stretch;

        Padding = new Thickness(0);
        Width = ButtonSize;
        Height = ButtonSize;
        ApplyCornerRadius();

        Cursor = new Cursor(StandardCursorType.Hand);

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

    public double ButtonSize
    {
        get => GetValue(ButtonSizeProperty);
        set => SetValue(ButtonSizeProperty, value);
    }

    public SVGIconButtonShape ButtonShape
    {
        get => GetValue(ButtonShapeProperty);
        set => SetValue(ButtonShapeProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == IconProperty)
        {
            _icon.Icon = Icon;
        }
        else if (change.Property == IconSizeProperty)
        {
            _icon.IconSize = IconSize;
        }
        else if (change.Property == ButtonSizeProperty)
        {
            Width = ButtonSize;
            Height = ButtonSize;
            ApplyCornerRadius();
        }
        else if (change.Property == ButtonShapeProperty)
        {
            ApplyCornerRadius();
        }
        else if (change.Property == BoundsProperty)
        {
            ApplyCornerRadius();
        }
    }

    private void ApplyCornerRadius() =>
        CornerRadius = ButtonShape == SVGIconButtonShape.Circle
            ? new CornerRadius(Math.Min(Bounds.Width, Bounds.Height) / 2)
            : default;
}
