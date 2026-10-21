using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace ChroaVideoPlayer.Plugins.Subtitle;

public static class AssParser
{
    private static readonly Regex PositionTag = new(@"\\pos\(\s*(-?\d+)\s*,\s*(-?\d+)\s*\)", RegexOptions.Compiled);

    private static readonly Regex InlineTag = new(@"\{[^}]*\}", RegexOptions.Compiled);

    public static IReadOnlyList<ComposedSource> Load(string path)
    {
        var groups = new List<(double X, double Y, List<SubtitleCue> Cues)>();

        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return Array.Empty<ComposedSource>();

        var lines = File.ReadAllLines(path, Encoding.UTF8);
        var playResX = 0.0;
        var playResY = 0.0;

        foreach (var raw in lines)
        {
            var line = raw.Trim();

            if (line.StartsWith("PlayResX:", StringComparison.OrdinalIgnoreCase)) playResX = Number(line[9..]);
            else if (line.StartsWith("PlayResY:", StringComparison.OrdinalIgnoreCase)) playResY = Number(line[9..]);
        }

        if (playResX <= 0) playResX = 1920;
        if (playResY <= 0) playResY = 1080;

        var inEvents = false;

        foreach (var raw in lines)
        {
            var line = raw.Trim();

            if (line.StartsWith("[", StringComparison.Ordinal))
            {
                inEvents = line.Equals("[Events]", StringComparison.OrdinalIgnoreCase);

                continue;
            }

            if (!inEvents || !line.StartsWith("Dialogue:", StringComparison.OrdinalIgnoreCase)) continue;

            var fields = line["Dialogue:".Length..].Split(',', 10);

            if (fields.Length < 10) continue;

            if (!TimeSpan.TryParseExact(fields[1].Trim(), @"h\:mm\:ss\.ff", CultureInfo.InvariantCulture, out var start)) continue;
            if (!TimeSpan.TryParseExact(fields[2].Trim(), @"h\:mm\:ss\.ff", CultureInfo.InvariantCulture, out var end)) continue;

            var text = fields[9];
            var position = PositionTag.Match(text);
            var x = position.Success && int.TryParse(position.Groups[1].Value, out var parsedX) ? parsedX : playResX / 2;
            var y = position.Success && int.TryParse(position.Groups[2].Value, out var parsedY) ? parsedY : playResY * 0.88;

            var clean = InlineTag.Replace(text, string.Empty).Trim();

            if (clean.Length == 0) continue;

            var xPercent = Math.Clamp(x / playResX * 100, 0, 100);
            var yPercent = Math.Clamp(y / playResY * 100, 0, 100);

            var index = groups.FindIndex(
                group => Math.Abs(group.X - xPercent) < 0.5 && Math.Abs(group.Y - yPercent) < 0.5);

            if (index < 0)
            {
                groups.Add((xPercent, yPercent, new List<SubtitleCue>()));
                index = groups.Count - 1;
            }

            groups[index].Cues.Add(new SubtitleCue(start, end, clean));
        }

        var sources = new List<ComposedSource>(groups.Count);

        for (var index = 0; index < groups.Count; index++)
        {
            var group = groups[index];

            sources.Add(new ComposedSource(
                $"ass:{path}|{index}",
                Path.GetFileNameWithoutExtension(path),
                group.Cues)
            {
                XPercent = group.X,
                YPercent = group.Y,
                HasPosition = true,
            });
        }

        return sources;
    }

    private static double Number(string value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result) ? result : 0;
}
