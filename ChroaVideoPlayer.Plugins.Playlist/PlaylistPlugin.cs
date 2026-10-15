using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using ChroaVideoPlayer.Core.EventBus;
using ChroaVideoPlayer.Core.Plugin;
using ChroaVideoPlayer.Core.Plugin.Context;
using ChroaVideoPlayer.Core.Plugin.UI;
using ChroaVideoPlayer.Plugins.Playlist.Persistance;

namespace ChroaVideoPlayer.Plugins.Playlist;

public sealed class PlaylistPlugin : IChroaPlugin, IChroaPluginEvents
{
    private readonly ObservableCollection<PlaylistEntry> _entries = new();

    private PlaylistStore? _store;

    private bool _loaded;

    public string Name => "playlist";

    public string Label => "Playlist";

    public string Icon => "avares://ChroaVideoPlayer.Plugins.Playlist/Icons/playlist.svg";

    public PluginLocation Location => PluginLocation.MainMenu;

    public bool Enabled => true;

    public ChroaApplicationContext? Context { get; set; }

    public void SubscribeEvents(IEventSubscriber events) => events.Subscribe<VideoPlayed>(OnVideoPlayed);

    public PluginWindow CreateWindow()
    {
        EnsureLoaded();

        var window = new PlaylistPluginWindow();

        window.ShowEntries(_entries);

        window.DeleteRequested += OnDeleteRequested;

        window.PlayRequested += OnPlayRequested;

        return window;
    }

    private PlaylistStore Store => _store ??= new PlaylistStore();

    private void EnsureLoaded()
    {
        if (_loaded) return;

        _loaded = true;

        foreach (var entry in Store.Load()) _entries.Add(entry);
    }

    private void OnVideoPlayed(VideoPlayed message)
    {
        EnsureLoaded();

        var existing = _entries.FirstOrDefault(entry => entry.Path == message.Path);

        if (existing is not null) _entries.Remove(existing);

        var record = new PlaylistEntry
        {
            Path = message.Path,
            Duration = message.Duration,
            LastPlayedUtc = DateTime.UtcNow,
        };

        foreach (var entry in _entries) entry.MarkPlaying(false);

        record.MarkPlaying(true);

        _entries.Insert(0, record);

        Store.Save(record);
    }

    private void OnDeleteRequested(IReadOnlyList<PlaylistEntry> entries)
    {
        foreach (var entry in entries)
        {
            _entries.Remove(entry);

            Store.Delete(entry.Path);
        }
    }

    private void OnPlayRequested(PlaylistEntry entry) => Context?.Mpv.Play(entry.Path);
}
