using ChroaVideoPlayer.Core.Persistance.Attributes;

namespace ChroaVideoPlayer.Plugins.Playlist.Persistance;

public sealed class PlaylistSettings
{
    [PersistentColumn]
    public bool AutoPlay { get; set; }
}
