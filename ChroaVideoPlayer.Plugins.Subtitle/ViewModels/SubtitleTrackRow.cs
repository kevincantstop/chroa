using ChroaVideoPlayer.Core.Mpv;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ChroaVideoPlayer.Plugins.Subtitle;

public partial class SubtitleTrackRow : ObservableObject
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

    [ObservableProperty]
    public partial bool IsCurrent { get; set; }

    public int Id { get; }

    public string Label { get; }

    public string Source { get; }

    public string Codec { get; }

    public bool IsSelected { get; }

    public string Tip { get; }
}
