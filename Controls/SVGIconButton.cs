using System;
using Avalonia;
using Avalonia.Controls;
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

/// <summary>
/// 图标按钮：Button 的外壳 + 一个 <see cref="SvgIcon"/> 当内容。
/// 图标的加载 / 缓存 / 24×24 描边规则全在 SvgIcon 里，这里只管"当按钮"
/// （尺寸、形状、铺满的命中区、手型指针）。
/// </summary>
public class SVGIconButton : Button
{
    /// <summary>图标来源，写法见 <see cref="SvgIcon.Icon"/>（"/Icons/x.svg" 或直接一段 path 数据）。</summary>
    public static readonly StyledProperty<string?> IconProperty =
        AvaloniaProperty.Register<SVGIconButton, string?>(nameof(Icon));

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

    /// <summary>真正画图标的东西 —— 加载、缓存、"24×24 + stroke 2"的约定都在它里面。</summary>
    private readonly SvgIcon _icon;

    /// <summary>
    /// 铺满整个按钮的透明层：由它负责命中测试和鼠标指针。
    /// 没有这一层的话，只有图标那一小块能被命中 —— 指针只在图标附近才变手型。
    /// </summary>
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
        // 局部值优先级高于样式，写在这里会让 Styles/SVGIconButton.axaml 里的
        // :pointerover 样式失效。外观一律交给样式文件。
    }

    /// <inheritdoc cref="IconProperty" />
    public string? Icon
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
            _icon.Icon = Icon;              // 解析 / 缓存 / 失败兜底都在 SvgIcon 里
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
            // 实际尺寸可能被布局改掉，所以圆角按真实 Bounds 算
            ApplyCornerRadius();
        }
    }

    private void ApplyCornerRadius() =>
        CornerRadius = ButtonShape == SVGIconButtonShape.Circle
            ? new CornerRadius(Math.Min(Bounds.Width, Bounds.Height) / 2)
            : default;
}
