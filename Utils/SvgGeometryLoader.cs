using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using Avalonia.Media;

namespace ChroaVideoPlayer.Utils;

internal static class SvgGeometryLoader
{
    public static Geometry? Parse(string svgText)
    {
        XDocument doc;
        try
        {
            doc = XDocument.Parse(svgText);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to parse SVG: {ex.Message}");
            return null;
        }

        var root = doc.Root;
        if (root == null) return null;

        WarnIfNonStandardViewBox(root);

        var parts = new List<string>();
        foreach (var el in root.Descendants())
        {
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

        var group = new GeometryGroup();
        foreach (var d in parts)
        {
            try
            {
                group.Children.Add(Geometry.Parse(d));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to parse SVG path: {ex.Message} (data={d})");
            }
        }

        return group.Children.Count > 0 ? group : null;
    }

    private static void WarnIfNonStandardViewBox(XElement root)
    {
        var vb = root.Attribute("viewBox")?.Value;
        if (string.IsNullOrWhiteSpace(vb)) return;

        var n = ParseNumbers(vb).Where(d => !double.IsNaN(d)).ToArray();
        if (n.Length != 4) return;
        if (Math.Abs(n[0]) < 0.001 && Math.Abs(n[1]) < 0.001 &&
            Math.Abs(n[2] - 24) < 0.001 && Math.Abs(n[3] - 24) < 0.001) return;

        Debug.WriteLine($"SVG viewBox is not '0 0 24 24' (actual {vb}); no auto-scaling is applied, the icon may render too large or too small.");
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
