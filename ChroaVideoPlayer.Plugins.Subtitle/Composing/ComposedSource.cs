using System.Collections.Generic;

namespace ChroaVideoPlayer.Plugins.Subtitle;

public sealed class ComposedSource
{
    public ComposedSource(string key, string label, IReadOnlyList<SubtitleCue> cues)
    {
        Key = key;
        Label = label;
        Cues = cues;
    }

    public string Key { get; }

    public string Label { get; }

    public IReadOnlyList<SubtitleCue> Cues { get; }

    public double XPercent { get; set; } = 50;

    public double YPercent { get; set; } = 88;
}
