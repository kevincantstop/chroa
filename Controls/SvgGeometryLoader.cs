using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using Avalonia.Media;

namespace ChroaVideoPlayer.Controls;

/// <summary>
/// 最小 SVG → Geometry 转换器：只处理图标类 SVG 会用到的几何元素
/// （path / rect / circle / ellipse / line / polyline / polygon），
/// 忽略颜色、样式、transform、文字等一切非几何内容。
///
/// 刻意不引入 Avalonia.Svg.Skia 这类完整 SVG 渲染库 —— 图标只需要几何。
/// </summary>
internal static class SvgGeometryLoader
{
    /// <summary>
    /// 把 SVG 文本里每个几何元素各自解析成一个 <see cref="Geometry"/>，再用 <see cref="GeometryGroup"/>
    /// 组合返回（不拼接成一条 path 数据 —— 理由见 Parse 内的注释）。没有几何元素时返回 null。
    /// </summary>
    public static Geometry? Parse(string svgText)
    {
        XDocument doc;
        try
        {
            doc = XDocument.Parse(svgText);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"SVG 解析失败: {ex.Message}");
            return null;
        }

        var root = doc.Root;
        if (root == null) return null;

        WarnIfNonStandardViewBox(root);

        var parts = new List<string>();
        foreach (var el in root.Descendants())
        {
            // 命名空间可能缺省，所以只比较本地名
            var d = el.Name.LocalName switch
            {
                "path" => el.Attribute("d")?.Value,
                "rect" => RectToPath(el),
                "circle" => EllipseToPath(el, isCircle: true),
                "ellipse" => EllipseToPath(el, isCircle: false),
                "line" => LineToPath(el),
                "polyline" => PointsToPath(el, close: false),
                "polygon" => PointsToPath(el, close: true),
                _ => null
            };

            if (!string.IsNullOrWhiteSpace(d)) parts.Add(d!.Trim());
        }

        if (parts.Count == 0) return null;

        // 关键：绝不能把多个元素拼成一条 path 数据再解析。
        // SVG 里"首位的小写 m"等价于绝对 M，但它后面跟着的隐式 lineto 仍是**相对**的；
        // 拼成一条后 m 不再是首位，这段相对语义会被当成绝对坐标读，终点就错了。
        // x.svg 正是这种：
        //   <path d="M18 6 6 18"/>  → (18,6) → (6,18)
        //   <path d="m6 6 12 12"/>  → 起点按绝对 (6,6)，终点相对位移 (12,12) → (18,18)
        // 拼起来写成 "M6 6 12 12" 会解析成 (6,6) → (12,12)，第二捺只剩一半（图标"缺一块"）。
        // 正确做法：每个元素各解析成独立几何，再用 GeometryGroup 组合 —— 相对语义互不干扰
        // （单独解析时当前点是原点，首位小写 m 自然等于绝对坐标）。
        var group = new GeometryGroup();
        foreach (var d in parts)
        {
            try
            {
                group.Children.Add(Geometry.Parse(d));
            }
            catch (Exception ex)
            {
                // 单个元素坏掉只丢它自己，不再像以前那样整枚图标一起废掉
                Debug.WriteLine($"SVG path 解析失败: {ex.Message}（data={d}）");
            }
        }

        return group.Children.Count > 0 ? group : null;
    }

    /// <summary>控件按 24×24 坐标系渲染。非 24 的 viewBox 目前只告警，不做缩放。</summary>
    private static void WarnIfNonStandardViewBox(XElement root)
    {
        var vb = root.Attribute("viewBox")?.Value;
        if (string.IsNullOrWhiteSpace(vb)) return;

        var n = ParseNumbers(vb).Where(d => !double.IsNaN(d)).ToArray();
        if (n.Length != 4) return;
        if (Math.Abs(n[0]) < 0.001 && Math.Abs(n[1]) < 0.001 &&
            Math.Abs(n[2] - 24) < 0.001 && Math.Abs(n[3] - 24) < 0.001) return;

        Debug.WriteLine($"SVG 的 viewBox 不是 0 0 24 24（实际 {vb}），当前不会自动缩放，图标可能过大或过小。");
    }

    private static double Num(XElement el, string name, double fallback = 0)
    {
        var v = el.Attribute(name)?.Value;
        return v != null && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : fallback;
    }

    private static string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    private static double[] ParseNumbers(string raw) =>
        raw.Split(new[] { ' ', ',', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
           .Select(s => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : double.NaN)
           .ToArray();

    /// <summary>rect → 路径，支持 rx/ry 圆角（Lucide 的 square、pause 都用它）。</summary>
    private static string RectToPath(XElement el)
    {
        var x = Num(el, "x");
        var y = Num(el, "y");
        var w = Num(el, "width");
        var h = Num(el, "height");
        var rx = Num(el, "rx", Num(el, "ry"));

        if (w <= 0 || h <= 0) return string.Empty;
        if (rx <= 0) return $"M{F(x)} {F(y)}H{F(x + w)}V{F(y + h)}H{F(x)}Z";

        var r = Math.Min(rx, Math.Min(w, h) / 2);
        return $"M{F(x + r)} {F(y)}" +
               $"H{F(x + w - r)}A{F(r)} {F(r)} 0 0 1 {F(x + w)} {F(y + r)}" +
               $"V{F(y + h - r)}A{F(r)} {F(r)} 0 0 1 {F(x + w - r)} {F(y + h)}" +
               $"H{F(x + r)}A{F(r)} {F(r)} 0 0 1 {F(x)} {F(y + h - r)}" +
               $"V{F(y + r)}A{F(r)} {F(r)} 0 0 1 {F(x + r)} {F(y)}Z";
    }

    /// <summary>circle / ellipse → 两段半圆弧。</summary>
    private static string EllipseToPath(XElement el, bool isCircle)
    {
        var cx = Num(el, "cx");
        var cy = Num(el, "cy");
        var rx = isCircle ? Num(el, "r") : Num(el, "rx");
        var ry = isCircle ? Num(el, "r") : Num(el, "ry");

        if (rx <= 0 || ry <= 0) return string.Empty;

        return $"M{F(cx - rx)} {F(cy)}" +
               $"A{F(rx)} {F(ry)} 0 1 0 {F(cx + rx)} {F(cy)}" +
               $"A{F(rx)} {F(ry)} 0 1 0 {F(cx - rx)} {F(cy)}Z";
    }

    private static string LineToPath(XElement el) =>
        $"M{F(Num(el, "x1"))} {F(Num(el, "y1"))}L{F(Num(el, "x2"))} {F(Num(el, "y2"))}";

    private static string PointsToPath(XElement el, bool close)
    {
        var raw = el.Attribute("points")?.Value;
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;

        var n = ParseNumbers(raw).Where(d => !double.IsNaN(d)).ToArray();
        if (n.Length < 4) return string.Empty;

        var sb = new StringBuilder();
        for (var i = 0; i + 1 < n.Length; i += 2)
        {
            sb.Append(i == 0 ? "M" : "L").Append(F(n[i])).Append(' ').Append(F(n[i + 1]));
            if (i + 2 < n.Length) sb.Append(' ');
        }
        if (close) sb.Append('Z');
        return sb.ToString();
    }
}
