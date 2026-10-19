using ChroaVideoPlayer.Core.Mpv;

namespace ChroaVideoPlayer.Plugins.Subtitle;

public sealed class SubtitleTrackRow
{
    public SubtitleTrackRow(SubtitleTrackInfo track)
    {
        Id = track.Id;
        Label = track.Label;
        Source = track.IsExternal ? "External" : "Embedded";
        Codec = track.Codec;
        IsSelected = track.IsSelected;
        Tip = track.Codec;
    }

    public int Id { get; }

    public string Label { get; }

    public string Source { get; }

    public string Codec { get; }

    public bool IsSelected { get; }

    public string Tip { get; }
}
