using System;

namespace ChroaVideoPlayer.Plugins.Playlist;

public sealed class PlaylistEntry
{
    public string Path { get; set; } = string.Empty;

    public TimeSpan Duration { get; set; }

    public DateTime LastPlayedUtc { get; set; }

    public string Title => System.IO.Path.GetFileName(Path);

    public string DurationText => Duration >= TimeSpan.FromHours(1)
        ? Duration.ToString(@"h\:mm\:ss")
        : Duration.ToString(@"m\:ss");
}
