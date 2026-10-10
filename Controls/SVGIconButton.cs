using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using HAlign = Avalonia.Layout.HorizontalAlignment;
using VAlign = Avalonia.Layout.VerticalAlignment;

namespace ChroaVideoPlayer.Controls;

/// <summary>图标按钮的形状。</summary>
public enum SVGIconButtonShape
{
    /// <summary>方形：圆角为 0。</summary>
    Square,

    /// <summary>正圆形：圆角取短边的一半。</summary>
    Circle,
}

public class SVGIconButton : Button
{
    public static readonly StyledProperty<Geometry?> IconProperty =
        AvaloniaProperty.Register<SVGIconButton, Geometry?>(nameof(Icon));

    /// <summary>图标渲染尺寸，默认 16。</summary>
    public static readonly StyledProperty<double> IconSizeProperty =
        AvaloniaProperty.Register<SVGIconButton, double>(nameof(IconSize), 16d);

    /// <summary>
    /// 按钮边长（正方形），默认 32。会同时设置 Width 和 Height ——
    /// 所以别另外写 Width/Height，那两个是局部值，会把它覆盖掉。
    /// </summary>
    public static readonly StyledProperty<double> ButtonSizeProperty =
        AvaloniaProperty.Register<SVGIconButton, double>(nameof(ButtonSize), 32d);

    /// <summary>形状，默认正圆。</summary>
    public static readonly StyledProperty<SVGIconButtonShape> ButtonShapeProperty =
        AvaloniaProperty.Register<SVGIconButton, SVGIconButtonShape>(
            nameof(ButtonShape), SVGIconButtonShape.Circle);

    /// <summary>
    /// 铺满整个按钮的透明层：由它负责命中测试和鼠标指针。
    /// 没有这一层的话，只有图标那一小块能被命中 —— 指针只在图标附近才变手型。
    /// </summary>
    private readonly Border _hitArea;
    private readonly Viewbox _viewbox;
    private readonly Path _path;

    public SVGIconButton()
    {
        _path = new Path
        {
            StrokeThickness = 2,                // 对应 SVG 的 stroke-width="2"
            StrokeLineCap = PenLineCap.Round,   // 对应 stroke-linecap="round"
            StrokeJoin = PenLineJoin.Round,     // 对应 stroke-linejoin="round"
        };
        _path.Bind(Shape.StrokeProperty, new Binding(nameof(Foreground)) { Source = this });

        _viewbox = new Viewbox
        {
            Width = IconSize,
            Height = IconSize,
            // 必须显式居中：内容槽比图标大时，只设 Width/Height 会贴在左上角
            HorizontalAlignment = HAlign.Center,
            VerticalAlignment = VAlign.Center,
            Child = new Canvas
            {
                Width = 24,     // Lucide 的 viewBox
                Height = 24,
                Children = { _path },
            },
        };

        // 透明但"有绘制"的背景才会参与命中测试（null 不参与）
        _hitArea = new Border
        {
            Background = Brushes.Transparent,
            HorizontalAlignment = HAlign.Stretch,
            VerticalAlignment = VAlign.Stretch,
            Child = _viewbox,
        };
        // 指针跟随按钮的 Cursor：单一来源，实例上写 Cursor="..." 即可覆盖
        _hitArea.Bind(InputElement.CursorProperty, new Binding(nameof(Cursor)) { Source = this });

        Content = _hitArea;

        // 主题若把内容对齐设成 Center，ContentPresenter 只会给内容图标那么大，
        // 这层就铺不满按钮了。这里强制 Stretch。
        HorizontalContentAlignment = HAlign.Stretch;
        VerticalContentAlignment = VAlign.Stretch;

        Padding = new Thickness(0);
        Width = ButtonSize;
        Height = ButtonSize;
        ApplyCornerRadius();

        // 鼠标指针默认值：用强类型枚举，避免 XAML 字符串走隐式 Parse 的运行时风险
        Cursor = new Cursor(StandardCursorType.Hand);

        // 刻意不在这里设外观类属性（Background / BorderThickness / Foreground / Margin）：
        // 局部值优先级高于样式，写在这里会让 Controls/SVGIconButton.axaml 里的
        // :pointerover 样式失效。外观一律交给样式文件。
    }

    /// <inheritdoc cref="IconProperty" />
    public Geometry? Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    /// <inheritdoc cref="IconSizeProperty" />
    public double IconSize
    {
        get => GetValue(IconSizeProperty);
        set => SetValue(IconSizeProperty, value);
    }

    /// <inheritdoc cref="ButtonSizeProperty" />
    public double ButtonSize
    {
        get => GetValue(ButtonSizeProperty);
        set => SetValue(ButtonSizeProperty, value);
    }

    /// <inheritdoc cref="ButtonShapeProperty" />
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
            _path.Data = Icon;
        }
        else if (change.Property == IconSizeProperty)
        {
            _viewbox.Width = _viewbox.Height = IconSize;
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
            // 实际尺寸可能被布局改掉，所以圆角按真实 Bounds 算
            ApplyCornerRadius();
        }
    }

    private void ApplyCornerRadius() =>
        CornerRadius = ButtonShape == SVGIconButtonShape.Circle
            ? new CornerRadius(Math.Min(Bounds.Width, Bounds.Height) / 2)
            : default;
}