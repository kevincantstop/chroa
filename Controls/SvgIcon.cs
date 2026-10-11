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

// 必须起别名：本文件同时 using 了 System.IO（为了 StreamReader），
// 那个名字空间里也有一个 Path（System.IO.Path），直接用 Path 会 CS0104 二义性。
using Path = Avalonia.Controls.Shapes.Path;

namespace ChroaVideoPlayer.Controls;

/// <summary>
/// 画一个单色线条图标：坐标系固定按 Lucide 的 24×24 理解，stroke-width 2、圆头、圆角连接，
/// 颜色跟随 <see cref="Foreground"/>（该属性会从父级继承，所以放进按钮里就是按钮的前景色）。
/// <para>
/// 只管"画"，不是按钮：
/// · 要一个图标按钮 → 用 <see cref="SVGIconButton"/>
/// · 要往自己的控件里放图标 → 直接用它（空状态的 Load Video 按钮就是这么用的）
/// </para>
/// </summary>
public class SvgIcon : Decorator
{
    /// <summary>
    /// 图标来源，两种写法：
    /// <list type="bullet">
    ///   <item>SVG 资源路径：<c>"/Icons/film.svg"</c>（相对本程序集）或完整的 <c>"avares://程序集/路径.svg"</c></item>
    ///   <item>直接写 path 数据：<c>"M5 12h14"</c>（即 Lucide SVG 里 &lt;path&gt; 的 d 属性内容）</item>
    /// </list>
    /// 判定规则很简单：以 <c>.svg</c> 结尾就当文件，否则当 path 数据。
    /// </summary>
    public static readonly StyledProperty<string?> IconProperty =
        AvaloniaProperty.Register<SvgIcon, string?>(nameof(Icon));

    /// <summary>图标渲染尺寸，默认 16。</summary>
    public static readonly StyledProperty<double> IconSizeProperty =
        AvaloniaProperty.Register<SvgIcon, double>(nameof(IconSize), 16d);

    /// <summary>
    /// 描边颜色。AddOwner 自 TextElement.Foreground，所以它是"可继承"的：
    /// 自己不设值时就从父级拿（Button 的 Foreground 就是这么传下来的）。
    /// </summary>
    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        TextElement.ForegroundProperty.AddOwner<SvgIcon>();

    /// <summary>按来源字符串缓存解析结果 —— 避免每次属性变化都重读文件、重解析。全进程共用一份。</summary>
    private static readonly Dictionary<string, Geometry?> IconCache = new();

    private readonly Viewbox _viewbox;
    private readonly Path _path;

    public SvgIcon()
    {
        _path = new Path
        {
            StrokeThickness = 2,                // 对应 SVG 的 stroke-width="2"
            StrokeLineCap = PenLineCap.Round,   // 对应 SVG 的 stroke-linecap="round"
            StrokeJoin = PenLineJoin.Round,     // 对应 SVG 的 stroke-linejoin="round"
        };
        // 颜色不写死：绑到自己的 Foreground（通常是继承来的），样式里改 Foreground 就能带动图标
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

        Child = _viewbox;

        // 图标是纯显示元素，不参与命中测试：点它就是点它所在的按钮
        IsHitTestVisible = false;
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

    /// <inheritdoc cref="ForegroundProperty" />
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

    /// <summary>
    /// 把 <see cref="Icon"/> 解析成几何：path 数据直接解析；".svg" 则从 Avalonia 资源里读出来再解析。
    /// 任何失败都只记一条调试输出并返回 null（图标空白），不抛异常、不影响窗口。
    /// </summary>
    private static Geometry? ResolveIcon(string? source)
    {
        if (string.IsNullOrWhiteSpace(source)) return null;

        // 不是 .svg 就当 path 数据（老写法，保持兼容）
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
            // "/Icons/x.svg" 这种相对写法，按本控件所在程序集解析成 avares://
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
