using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using ChroaVideoPlayer.Core.Plugin.UI;
using ChroaVideoPlayer.Core.Utils;

namespace ChroaVideoPlayer.Plugins.Playlist;

public partial class PlaylistPluginWindow : PluginWindow
{
    private ObservableCollection<PlaylistEntry>? _entries;

    private readonly ObservableCollection<PlaylistEntry> _view = new();

    private bool _autoPlay;

    public PlaylistPluginWindow()
    {
        InitializeComponent();
    }

    public event Action<IReadOnlyList<PlaylistEntry>>? DeleteRequested;

    public event Action<PlaylistEntry, IReadOnlyList<string>>? PlayRequested;

    public event Action<bool>? AutoPlayChanged;

    public event Action<IReadOnlyList<string>>? AddRequested;

    public void ShowEntries(ObservableCollection<PlaylistEntry> entries)
    {
        if (_entries is not null) _entries.CollectionChanged -= OnEntriesChanged;

        _entries = entries;

        entries.CollectionChanged += OnEntriesChanged;

        foreach (var entry in entries) entry.MarkMissing(!File.Exists(entry.Path));

        ItemHost.ItemsSource = _view;

        RebuildView(clearSelection: false);
    }

    public void SetAutoPlay(bool autoPlay)
    {
        _autoPlay = autoPlay;

        ApplyAutoPlay();
    }

    private void OnSearchChanged(object? sender, TextChangedEventArgs e) => RebuildView(clearSelection: true);

    private void OnAutoPlayClick(object? sender, RoutedEventArgs e)
    {
        _autoPlay = !_autoPlay;

        ApplyAutoPlay();

        AutoPlayChanged?.Invoke(_autoPlay);
    }

    private void ApplyAutoPlay()
    {
        AutoPlayButton.Icon = _autoPlay
            ? "avares://ChroaVideoPlayer.Plugins.Playlist/Icons/play-next.svg"
            : "avares://ChroaVideoPlayer.Plugins.Playlist/Icons/play-single.svg";

        ToolTip.SetTip(AutoPlayButton, _autoPlay
            ? "Play the next video automatically"
            : "Play one video and stop");
    }

    private List<string> VisiblePaths() => _view.Select(entry => entry.Path).ToList();

    private void RebuildView(bool clearSelection)
    {
        if (clearSelection && _entries is { } all)
            foreach (var entry in all) entry.MarkSelected(false);

        _view.Clear();

        if (_entries is not { } entries)
        {
            RefreshState();

            return;
        }

        var query = SearchInput.Text?.Trim();

        foreach (var entry in entries)
            if (string.IsNullOrEmpty(query) || Matches(entry, query))
                _view.Add(entry);

        RefreshState();
    }

    private static bool Matches(PlaylistEntry entry, string query) =>
        entry.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
        entry.Path.Contains(query, StringComparison.OrdinalIgnoreCase);

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    private async void OnAddClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
            if (storage is null) return;

            var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Add to Playlist",
                AllowMultiple = true,
                FileTypeFilter = VideoFileTypes.Filter,
            });

            var paths = LocalPaths(files);

            if (paths.Count == 0) return;

            AddRequested?.Invoke(paths);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Add to playlist picker failed: {ex}");
        }
    }

    private void OnFileDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = LocalPaths(e.DataTransfer.TryGetFiles()).Count > 0
            ? DragDropEffects.Copy
            : DragDropEffects.None;

        e.Handled = true;
    }

    private void OnFileDrop(object? sender, DragEventArgs e)
    {
        e.Handled = true;

        var paths = LocalPaths(e.DataTransfer.TryGetFiles());

        if (paths.Count == 0) return;

        AddRequested?.Invoke(paths);
    }

    private static List<string> LocalPaths(IEnumerable<IStorageItem>? items)
    {
        var paths = new List<string>();

        if (items is null) return paths;

        foreach (var item in items)
        {
            var path = item.TryGetLocalPath();

            if (!string.IsNullOrEmpty(path)) paths.Add(path);
        }

        return paths;
    }

    private void OnRevealClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: PlaylistEntry entry }) return;

        Reveal(entry.Path);
    }

    private void OnDeleteClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: PlaylistEntry entry }) return;

        DeleteRequested?.Invoke([entry]);
    }

    private void OnDeleteSelectedClick(object? sender, RoutedEventArgs e)
    {
        if (_entries is not { } entries) return;

        var selected = entries.Where(entry => entry.IsSelected).ToList();

        if (selected.Count == 0) return;

        DeleteRequested?.Invoke(selected);
    }

    private void OnRowCheckClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox { DataContext: PlaylistEntry entry } box) return;

        entry.MarkSelected(box.IsChecked == true);

        RefreshState();
    }

    private void OnSelectAllClick(object? sender, RoutedEventArgs e)
    {
        var select = SelectAllBox.IsChecked == true;

        foreach (var entry in _view) entry.MarkSelected(select);

        RefreshState();
    }

    private void OnEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e) => RebuildView(clearSelection: false);

    private void RefreshState()
    {
        var total = _entries?.Count ?? 0;
        var selected = _entries?.Count(entry => entry.IsSelected) ?? 0;

        CountText.Text = selected > 0
            ? $"{selected} of {total} {Count(total)}"
            : $"{total} {Count(total)}";

        DeleteSelectedButton.IsVisible = selected > 0;

        if (selected == 0 || total == 0) SelectAllBox.IsChecked = false;
        else if (selected == total) SelectAllBox.IsChecked = true;
        else SelectAllBox.IsChecked = null;
    }

    private static string Count(int total) => total == 1 ? "video" : "videos";

    private void OnRowDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (e.Source is Visual source && source.FindAncestorOfType<Button>(true) is not null) return;
        if (sender is not Grid { DataContext: PlaylistEntry entry }) return;

        entry.MarkMissing(!File.Exists(entry.Path));

        if (entry.IsMissing) return;

        PlayRequested?.Invoke(entry, VisiblePaths());
    }

    private void OnRowContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (sender is not Grid { DataContext: PlaylistEntry entry }) return;

        entry.MarkMissing(!File.Exists(entry.Path));

        RowMenu.Entry = entry;

        if (entry.IsSelected) return;

        if (_entries is { } entries)
            foreach (var other in entries) other.MarkSelected(false);

        entry.MarkSelected(true);

        RefreshState();
    }

    private void OnMenuPlayRequested(PlaylistEntry entry) => PlayRequested?.Invoke(entry, VisiblePaths());

    private void OnMenuRevealRequested(PlaylistEntry entry) => Reveal(entry.Path);

    private void OnMenuDeleteRequested(PlaylistEntry entry) => DeleteRequested?.Invoke([entry]);

    private static void Reveal(string path)
    {
        if (!OperatingSystem.IsWindows()) return;

        try
        {
            var target = File.Exists(path) ? path : Path.GetDirectoryName(path);

            if (string.IsNullOrEmpty(target)) return;
            if (!File.Exists(target) && !Directory.Exists(target)) return;

            var arguments = File.Exists(target) ? $"/select,\"{target}\"" : $"\"{target}\"";

            Process.Start(new ProcessStartInfo("explorer.exe", arguments) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Could not open the file location for {path}: {ex.Message}");
        }
    }
}
