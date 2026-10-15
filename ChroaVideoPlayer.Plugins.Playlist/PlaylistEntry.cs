namespace ChroaVideoPlayer.Plugins.Playlist;

public sealed record PlaylistEntry(string Path)
{
    public string Title => System.IO.Path.GetFileName(Path);
}
