using System;
using System.Collections.Generic;
using System.Text;

namespace ChroaVideoPlayer.Plugins.Subtitle;

public sealed record ComposedTrack(string Name, IReadOnlyList<SubtitleCue> Cues);

public static class AssComposer
{
    private const double BottomPercent = 88;

    private const double SlotGapPercent = 20;

    private const string StyleFormat =
        "Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding";

    public static string Build(IReadOnlyList<ComposedTrack> tracks, int width, int height, string title)
    {
        var playResX = width > 0 ? width : 1920;
        var playResY = height > 0 ? height : 1080;

        var builder = new StringBuilder();

        builder.AppendLine("[Script Info]");
        builder.AppendLine($"Title: {title}");
        builder.AppendLine("ScriptType: v4.00+");
        builder.AppendLine($"PlayResX: {playResX}");
        builder.AppendLine($"PlayResY: {playResY}");
        builder.AppendLine("WrapStyle: 2");
        builder.AppendLine("ScaledBorderAndShadow: yes");
        builder.AppendLine();
        builder.AppendLine("[V4+ Styles]");
        builder.AppendLine(StyleFormat);

        for (var index = 0; index < tracks.Count; index++) builder.AppendLine(StyleLine(StyleName(index)));

        builder.AppendLine();
        builder.AppendLine("[Events]");
        builder.AppendLine("Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text");

        for (var trackIndex = 0; trackIndex < tracks.Count; trackIndex++)
        {
            var y = playResY * SlotPercent(trackIndex, tracks.Count) / 100;

            foreach (var cue in tracks[trackIndex].Cues)
            {
                builder.AppendLine(
                    $"Dialogue: {trackIndex},{FormatTime(cue.Start)},{FormatTime(cue.End)},{StyleName(trackIndex)},,0,0,0,,{{\\an5\\pos({playResX / 2},{y})}}{cue.Text}");
            }
        }

        return builder.ToString();
    }

    private static double SlotPercent(int index, int count) =>
        Math.Max(8, BottomPercent - (count - 1 - index) * SlotGapPercent);

    private static string StyleName(int index) => $"Track{index + 1}";

    private static string StyleLine(string name) =>
        $"Style: {name},Arial,48,&H00FFFFFF,&H000000FF,&H00000000,&H00000000,0,0,0,0,100,100,0,0,1,2,0,5,10,10,10,1";

    private static string FormatTime(TimeSpan value) =>
        $"{(int)value.TotalHours}:{value.Minutes:00}:{value.Seconds:00}.{value.Milliseconds / 10:00}";
}
