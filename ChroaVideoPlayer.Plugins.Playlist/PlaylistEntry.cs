using System;

namespace ChroaVideoPlayer.Plugins.Playlist;

public sealed record PlaylistEntry(string Path, TimeSpan Duration)
{
    public string Title => System.IO.Path.GetFileName(Path);

    public string DurationText => Duration >= TimeSpan.FromHours(1)
        ? Duration.ToString(@"h\:mm\:ss")
        : Duration.ToString(@"m\:ss");
}
