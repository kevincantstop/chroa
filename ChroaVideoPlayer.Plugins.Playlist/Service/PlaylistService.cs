using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using ChroaVideoPlayer.Core.Utils;
using ChroaVideoPlayer.Plugins.Playlist.Persistance;

namespace ChroaVideoPlayer.Plugins.Playlist.Service;

public sealed class PlaylistService
{
    private PlaylistStore? _store;

    private PlaylistSettingsStore? _settingsStore;

    private List<string>? _queue;

    private int _queueIndex = -1;

    private string? _expectedPath;

    private string? _currentPath;

    private const int ProgressSaveIntervalMs = 5000;

    private readonly Stopwatch _progressClock = Stopwatch.StartNew();

    private bool _loaded;

    private bool _autoPlay = true;

    public event Action<string, TimeSpan>? PlaybackRequested;

    public event Action<bool>? AutoPlayChanged;

    public ObservableCollection<PlaylistEntry> Entries { get; } = new();

    public bool AutoPlay => _autoPlay;

    public void EnsureLoaded()
    {
        if (_loaded) return;

        _loaded = true;

        foreach (var entry in Store.Load()) Entries.Add(entry);

        _autoPlay = SettingsStore.LoadAutoPlay();
    }

    public PlaylistEntry? Find(string path) => string.IsNullOrEmpty(path)
        ? null
        : Entries.FirstOrDefault(entry => entry.Path == path);

    public IReadOnlyList<PlaylistEntry> Search(string? query)
    {
        var text = query?.Trim();

        if (string.IsNullOrEmpty(text)) return Entries.ToList();

        return Entries.Where(entry => Matches(entry, text)).ToList();
    }

    public bool IsFull => Entries.Count >= PlaylistStore.MaxEntries;

    public bool Add(string path)
    {
        if (CreateEntry(path) is not { } entry) return false;

        Store.Save(entry);

        return true;
    }

    public PlaylistAddResult AddRange(IEnumerable<string> paths)
    {
        var added = new List<PlaylistEntry>();
        var rejected = 0;

        foreach (var path in paths)
        {
            if (CreateEntry(path) is { } entry) added.Add(entry);
            else if (Find(path) is null) rejected++;
        }

        Store.SaveRange(added);

        return new PlaylistAddResult(added.Count, rejected);
    }

    public PlaylistAddResult AddFolder(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath)) return default;
        if (!Directory.Exists(folderPath)) return default;

        var files = Directory
            .EnumerateFiles(folderPath, "*", SearchOption.TopDirectoryOnly)
            .Where(VideoFileTypes.IsVideoFile)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);

        return AddRange(files);
    }

    public bool Remove(string path)
    {
        if (Find(path) is not { } entry) return false;

        Entries.Remove(entry);

        Store.Delete(entry.Path);

        return true;
    }

    public int RemoveRange(IEnumerable<string> paths)
    {
        var count = 0;

        foreach (var path in paths)
            if (Remove(path)) count++;

        return count;
    }

    public bool Move(int from, int to)
    {
        if (from < 0 || from >= Entries.Count) return false;
        if (to < 0 || to >= Entries.Count) return false;
        if (from == to) return false;

        return MoveRange([Entries[from]], to);
    }

    public bool MoveRange(IReadOnlyList<PlaylistEntry> entries, int to)
    {
        var moving = Entries.Where(entries.Contains).ToList();

        if (moving.Count == 0) return false;
        if (moving.Count == Entries.Count) return false;

        foreach (var entry in moving) Entries.Remove(entry);

        if (to < 0) to = 0;
        if (to > Entries.Count) to = Entries.Count;

        for (var index = 0; index < moving.Count; index++) Entries.Insert(to + index, moving[index]);

        Store.SaveOrder(Entries);

        return true;
    }

    public bool Move(string path, int offset)
    {
        if (Find(path) is not { } entry) return false;

        var index = Entries.IndexOf(entry);

        return Move(index, index + offset);
    }

    public bool Reveal(string path)
    {
        if (!OperatingSystem.IsWindows()) return false;

        try
        {
            var target = File.Exists(path) ? path : Path.GetDirectoryName(path);

            if (string.IsNullOrEmpty(target)) return false;
            if (!File.Exists(target) && !Directory.Exists(target)) return false;

            var arguments = File.Exists(target) ? $"/select,\"{target}\"" : $"\"{target}\"";

            Process.Start(new ProcessStartInfo("explorer.exe", arguments) { UseShellExecute = true });

            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Could not open the file location for {path}: {ex.Message}");

            return false;
        }
    }

    public bool Play(string path, IReadOnlyList<string>? queue = null)
    {
        if (Find(path) is not { } entry) return false;

        entry.MarkMissing(!File.Exists(path));

        if (entry.IsMissing) return false;

        _queue = (queue ?? Entries.Select(item => item.Path)).ToList();
        _queueIndex = _queue.IndexOf(path);
        _expectedPath = path;

        PlaybackRequested?.Invoke(path, entry.ResumePosition);

        return true;
    }

    public PlaylistEntry? Track(string path, TimeSpan duration)
    {
        if (EnsureEntry(path) is not { } entry) return null;

        if (duration > TimeSpan.Zero) entry.MarkDuration(duration);

        entry.LastPlayedUtc = DateTime.UtcNow;

        Store.Save(entry);

        return entry;
    }

    public void HandleVideoPlayed(string path, TimeSpan duration)
    {
        EnsureLoaded();

        if (path != _expectedPath) ClearQueue();

        _currentPath = path;

        foreach (var entry in Entries) entry.MarkPlaying(false);

        Track(path, duration)?.MarkPlaying(true);
    }

    public void HandleVideoEnded(string path)
    {
        if (Find(path) is { } entry)
        {
            entry.MarkPlayed(entry.Duration.TotalSeconds);

            Store.Save(entry);
        }

        if (!_autoPlay) return;
        if (_queue is not { } queue || path != _expectedPath) return;

        PlayNext(queue);
    }

    public void HandleVideoProgress(string path, TimeSpan position, TimeSpan duration)
    {
        if (Find(path) is not { } entry) return;

        if (duration > TimeSpan.Zero && entry.Duration != duration) entry.MarkDuration(duration);

        entry.MarkPlayed(position.TotalSeconds);

        if (_progressClock.ElapsedMilliseconds < ProgressSaveIntervalMs) return;

        _progressClock.Restart();

        Store.Save(entry);
    }

    public bool Step(int offset)
    {
        if (offset == 0) return false;

        if (_queue is { } queue && _queueIndex >= 0)
        {
            for (var index = _queueIndex + offset; index >= 0 && index < queue.Count; index += offset)
            {
                if (TryPlayAt(index, queue[index])) return true;
            }

            return false;
        }

        if (_currentPath is null || Find(_currentPath) is not { } current) return false;

        var start = Entries.IndexOf(current);

        if (start < 0) return false;

        for (var index = start + offset; index >= 0 && index < Entries.Count; index += offset)
        {
            if (TryPlayAt(index, Entries[index].Path)) return true;
        }

        return false;
    }

    public void SetAutoPlay(bool autoPlay)
    {
        if (_autoPlay == autoPlay) return;

        _autoPlay = autoPlay;

        SettingsStore.SaveAutoPlay(autoPlay);

        AutoPlayChanged?.Invoke(autoPlay);
    }

    private PlaylistStore Store => _store ??= new PlaylistStore();

    private PlaylistSettingsStore SettingsStore => _settingsStore ??= new PlaylistSettingsStore();

    private PlaylistEntry? EnsureEntry(string path) => Find(path) ?? CreateEntry(path);

    private bool TryPlayAt(int index, string path)
    {
        if (Find(path) is not { } entry) return false;

        entry.MarkMissing(!File.Exists(path));

        if (entry.IsMissing) return false;

        if (_queue is not null) _queueIndex = index;

        _expectedPath = path;

        PlaybackRequested?.Invoke(path, entry.ResumePosition);

        return true;
    }

    private PlaylistEntry? CreateEntry(string path)
    {
        if (Find(path) is not null) return null;
        if (string.IsNullOrWhiteSpace(path)) return null;
        if (IsFull) return null;

        var entry = new PlaylistEntry
        {
            Path = path,
            AddedUtc = DateTime.UtcNow,
            SortIndex = NextSortIndex(),
        };

        Entries.Add(entry);

        return entry;
    }

    private int NextSortIndex() => Entries.Count == 0 ? 1 : Entries.Max(entry => entry.SortIndex) + 1;

    private static bool Matches(PlaylistEntry entry, string query) =>
        entry.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
        entry.Path.Contains(query, StringComparison.OrdinalIgnoreCase);

    private void PlayNext(List<string> queue)
    {
        for (var index = _queueIndex + 1; index < queue.Count; index++)
        {
            _queueIndex = index;

            var path = queue[index];

            if (path == _expectedPath) continue;

            var entry = Find(path);

            if (entry is null) continue;

            entry.MarkMissing(!File.Exists(path));

            if (entry.IsMissing) continue;

            _expectedPath = path;

            PlaybackRequested?.Invoke(path, entry.ResumePosition);

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
}

public readonly record struct PlaylistAddResult(int Added, int Rejected);
