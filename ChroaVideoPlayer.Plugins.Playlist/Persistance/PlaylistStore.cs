using System;
using System.Collections.Generic;
using System.IO;
using ChroaVideoPlayer.Core.Persistance;

namespace ChroaVideoPlayer.Plugins.Playlist.Persistance;

public sealed class PlaylistStore
{
    private const string DatabaseFileName = "playlist.db";

    private const int MaxEntries = 200;

    private readonly LocalStore<PlaylistEntry> _store;

    public PlaylistStore(string? databasePath = null) =>
        _store = new LocalStore<PlaylistEntry>(
            MaxEntries,
            nameof(PlaylistEntry.LastPlayedUtc),
            databasePath ?? DefaultPath);

    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ChroaVideoPlayer",
        DatabaseFileName);

    public IReadOnlyList<PlaylistEntry> Load() =>
        _store.Query(orderBy: nameof(PlaylistEntry.LastPlayedUtc), descending: true, limit: MaxEntries);

    public void Save(PlaylistEntry entry) => _store.Save(entry.Path, entry);
}
