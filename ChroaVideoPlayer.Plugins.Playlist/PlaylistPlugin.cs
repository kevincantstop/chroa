using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using ChroaVideoPlayer.Core.EventBus;
using ChroaVideoPlayer.Core.EventBus.Events;
using ChroaVideoPlayer.Core.Plugin;
using ChroaVideoPlayer.Core.Plugin.Context;
using ChroaVideoPlayer.Core.Plugin.UI;
using ChroaVideoPlayer.Plugins.Playlist.Persistance;

namespace ChroaVideoPlayer.Plugins.Playlist;

public sealed class PlaylistPlugin : IChroaPlugin, IChroaPluginEvents
{
    private readonly ObservableCollection<PlaylistEntry> _entries = new();

    private PlaylistStore? _store;

    private PlaylistSettingsStore? _settingsStore;

    private List<string>? _queue;

    private int _queueIndex = -1;

    private string? _expectedPath;

    private bool _loaded;

    private bool _autoPlay = true;

    public string Name => "playlist";

    public string Label => "Playlist";

    public string Icon => "avares://ChroaVideoPlayer.Plugins.Playlist/Icons/playlist.svg";

    public PluginLocation Location => PluginLocation.MainMenu;

    public bool Enabled => true;

    public ChroaApplicationContext? Context { get; set; }

    public void SubscribeEvents(IEventSubscriber events)
    {
        events.Subscribe<VideoPlayed>(OnVideoPlayed);

        events.Subscribe<VideoEnded>(OnVideoEnded);
    }

    public PluginWindow CreateWindow()
    {
        EnsureLoaded();

        var window = new PlaylistPluginWindow();

        window.ShowEntries(_entries);

        window.SetAutoPlay(_autoPlay);

        window.DeleteRequested += OnDeleteRequested;

        window.PlayRequested += OnPlayRequested;

        window.AddRequested += OnAddRequested;

        window.AutoPlayChanged += OnAutoPlayChanged;

        return window;
    }

    private PlaylistStore Store => _store ??= new PlaylistStore();

    private PlaylistSettingsStore SettingsStore => _settingsStore ??= new PlaylistSettingsStore();

    private void EnsureLoaded()
    {
        if (_loaded) return;

        _loaded = true;

        foreach (var entry in Store.Load()) _entries.Add(entry);

        _autoPlay = SettingsStore.LoadAutoPlay();
    }

    private void OnVideoPlayed(VideoPlayed message)
    {
        EnsureLoaded();

        if (message.Path != _expectedPath) ClearQueue();

        foreach (var entry in _entries) entry.MarkPlaying(false);

        Insert(message.Path, message.Duration).MarkPlaying(true);
    }

    private void OnVideoEnded(VideoEnded message)
    {
        if (!_autoPlay) return;
        if (_queue is not { } queue || message.Path != _expectedPath) return;

        Advance(queue);
    }

    private void Advance(List<string> queue)
    {
        for (var index = _queueIndex + 1; index < queue.Count; index++)
        {
            _queueIndex = index;

            var path = queue[index];

            if (path == _expectedPath) continue;

            var entry = _entries.FirstOrDefault(item => item.Path == path);

            if (entry is null) continue;

            entry.MarkMissing(!File.Exists(path));

            if (entry.IsMissing) continue;

            _expectedPath = path;

            Context?.Mpv.Play(path);

            return;
        }

        ClearQueue();
    }

    private void ClearQueue()
    {
        _queue = null;
        _queueIndex = -1;
        _expectedPath = null;
    }

    private void OnAutoPlayChanged(bool autoPlay)
    {
        _autoPlay = autoPlay;

        SettingsStore.SaveAutoPlay(autoPlay);
    }

    private void OnPlayRequested(PlaylistEntry entry, IReadOnlyList<string> order)
    {
        _queue = order.ToList();
        _queueIndex = _queue.IndexOf(entry.Path);
        _expectedPath = entry.Path;

        Context?.Mpv.Play(entry.Path);
    }

    private void OnAddRequested(IReadOnlyList<string> paths)
    {
        EnsureLoaded();

        var playing = _entries.FirstOrDefault(entry => entry.IsPlaying)?.Path;

        foreach (var path in paths) Insert(path, TimeSpan.Zero);

        _entries.FirstOrDefault(entry => entry.Path == playing)?.MarkPlaying(true);
    }

    private PlaylistEntry Insert(string path, TimeSpan duration)
    {
        var existing = _entries.FirstOrDefault(entry => entry.Path == path);

        if (existing is not null) _entries.Remove(existing);

        var record = new PlaylistEntry
        {
            Path = path,
            Duration = duration,
            LastPlayedUtc = DateTime.UtcNow,
        };

        _entries.Insert(0, record);

        Store.Save(record);

        return record;
    }

    private void OnDeleteRequested(IReadOnlyList<PlaylistEntry> entries)
    {
        foreach (var entry in entries)
        {
            _entries.Remove(entry);

            Store.Delete(entry.Path);
        }
    }
}
