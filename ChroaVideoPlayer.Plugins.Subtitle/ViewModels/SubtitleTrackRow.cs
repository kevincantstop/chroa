using ChroaVideoPlayer.Core.Mpv;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ChroaVideoPlayer.Plugins.Subtitle;

public partial class SubtitleTrackRow : ObservableObject
{
    public SubtitleTrackRow(SubtitleTrackInfo track)
    {
        Info = track;
        Id = track.Id;
        Label = track.Label;
        Source = track.IsExternal ? "External" : "Embedded";
        IsExternal = track.IsExternal;
        IsSelected = track.IsSelected;
        Tip = track.Codec;
    }

    [ObservableProperty]
    public partial bool IsCurrent { get; set; }

    [ObservableProperty]
    public partial bool IsIncluded { get; set; }

    [ObservableProperty]
    public partial bool CanPosition { get; set; }

    public SubtitleTrackInfo Info { get; }

    public string ExternalFileName => Info.ExternalFileName;

    public int Id { get; }

    public string Label { get; }

    public string Source { get; }

    public bool IsExternal { get; }

    public bool IsSelected { get; }

    public string Tip { get; }
}
