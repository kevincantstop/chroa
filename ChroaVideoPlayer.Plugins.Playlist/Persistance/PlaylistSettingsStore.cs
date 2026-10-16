using ChroaVideoPlayer.Core.Persistance;

namespace ChroaVideoPlayer.Plugins.Playlist.Persistance;

public sealed class PlaylistSettingsStore
{
    private const string Key = "settings";

    private readonly LocalStore<PlaylistSettings> _store;

    public PlaylistSettingsStore(string? databasePath = null) =>
        _store = new LocalStore<PlaylistSettings>(databasePath ?? PlaylistStore.DefaultPath);

    public bool LoadAutoPlay() => _store.Get(Key)?.AutoPlay ?? true;

    public void SaveAutoPlay(bool autoPlay) => _store.Save(Key, new PlaylistSettings { AutoPlay = autoPlay });
}
