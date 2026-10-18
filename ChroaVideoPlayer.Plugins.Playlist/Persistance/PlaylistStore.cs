using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ChroaVideoPlayer.Core.Persistance;

namespace ChroaVideoPlayer.Plugins.Playlist.Persistance;

public sealed class PlaylistStore
{
    private const string DatabaseFileName = "playlist.db";

    public const int MaxEntries = 500;

    private readonly LocalStore<PlaylistEntry> _store;

    public PlaylistStore(string? databasePath = null) =>
        _store = new LocalStore<PlaylistEntry>(
            MaxEntries,
            nameof(PlaylistEntry.AddedUtc),
            databasePath ?? DefaultPath);

    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ChroaVideoPlayer",
        DatabaseFileName);

    public IReadOnlyList<PlaylistEntry> Load()
    {
        var rows = _store.Query(orderBy: nameof(PlaylistEntry.AddedUtc), limit: MaxEntries).ToList();

        if (rows.Any(entry => entry.SortIndex <= 0)) SaveOrder(rows);

        return rows.OrderBy(entry => entry.SortIndex).ToList();
    }

    public void Save(PlaylistEntry entry) => _store.Save(entry.Path, entry);

    public void SaveOrder(IReadOnlyList<PlaylistEntry> entries)
    {
        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];

            if (entry.SortIndex == index + 1) continue;

            entry.SortIndex = index + 1;

            _store.Save(entry.Path, entry);
        }
    }

    public void Delete(string path) => _store.Delete(path);
}
