using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace ChroaVideoPlayer.Plugins.Subtitle;

public static class SrtParser
{
    private static readonly Regex TimeLine = new(
        @"(?<h>\d+):(?<m>\d{1,2}):(?<s>\d{1,2})[,.](?<ms>\d{1,3})\s*-->\s*(?<h2>\d+):(?<m2>\d{1,2}):(?<s2>\d{1,2})[,.](?<ms2>\d{1,3})",
        RegexOptions.Compiled);

    private static readonly Regex ItalicOpen = new(@"<\s*i\s*>", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex ItalicClose = new(@"<\s*/\s*i\s*>", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex BoldOpen = new(@"<\s*b\s*>", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex BoldClose = new(@"<\s*/\s*b\s*>", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex AnyTag = new("<[^>]+>", RegexOptions.Compiled);

    public static IReadOnlyList<SubtitleCue> Load(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return Array.Empty<SubtitleCue>();

        var lines = ReadLines(path);
        var cues = new List<SubtitleCue>();
        var index = 0;

        while (index < lines.Length)
        {
            var match = TimeLine.Match(lines[index]);

            if (!match.Success)
            {
                index++;

                continue;
            }

            var start = ToTimeSpan(match, "h", "m", "s", "ms");
            var end = ToTimeSpan(match, "h2", "m2", "s2", "ms2");

            index++;

            var text = new StringBuilder();

            while (index < lines.Length && !string.IsNullOrWhiteSpace(lines[index]))
            {
                if (text.Length > 0) text.Append("\\N");

                text.Append(Clean(lines[index]));

                index++;
            }

            if (text.Length > 0) cues.Add(new SubtitleCue(start, end, text.ToString()));
        }

        return cues;
    }

    private static string[] ReadLines(string path)
    {
        var text = Decode(File.ReadAllBytes(path));

        return text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
    }

    private static string Decode(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);

        try
        {
            return new UTF8Encoding(false, true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.GetEncoding(936).GetString(bytes);
        }
    }

    private static TimeSpan ToTimeSpan(Match match, string hours, string minutes, string seconds, string millis)
    {
        var value = match.Groups[millis].Value;

        while (value.Length < 3) value += "0";

        return new TimeSpan(
            0,
            int.Parse(match.Groups[hours].Value, CultureInfo.InvariantCulture),
            int.Parse(match.Groups[minutes].Value, CultureInfo.InvariantCulture),
            int.Parse(match.Groups[seconds].Value, CultureInfo.InvariantCulture),
            int.Parse(value, CultureInfo.InvariantCulture));
    }

    private static string Clean(string line)
    {
        var text = line.Trim()
            .Replace('{', '｛')
            .Replace('}', '｝');

        text = ItalicClose.Replace(text, "{\\i0}");
        text = ItalicOpen.Replace(text, "{\\i1}");
        text = BoldClose.Replace(text, "{\\b0}");
        text = BoldOpen.Replace(text, "{\\b1}");

        return AnyTag.Replace(text, string.Empty);
    }
}
