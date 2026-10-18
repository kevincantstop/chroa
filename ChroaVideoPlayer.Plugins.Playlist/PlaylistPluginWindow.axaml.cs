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
using ChroaVideoPlayer.Plugins.Playlist.Service;

namespace ChroaVideoPlayer.Plugins.Playlist;

public partial class PlaylistPluginWindow : PluginWindow
{
    private PlaylistService? _service;

    private PlaylistEntry? _current;

    private readonly ObservableCollection<PlaylistEntry> _view = new();

    private bool _autoPlay;

    public PlaylistPluginWindow()
    {
        InitializeComponent();
    }

    public void ShowService(PlaylistService service)
    {
        if (_service is not null)
        {
            _service.Entries.CollectionChanged -= OnEntriesChanged;
            _service.AutoPlayChanged -= OnAutoPlayStateChanged;
        }

        _service = service;

        service.EnsureLoaded();

        service.Entries.CollectionChanged += OnEntriesChanged;
        service.AutoPlayChanged += OnAutoPlayStateChanged;

        foreach (var entry in service.Entries) entry.MarkMissing(!File.Exists(entry.Path));

        ItemHost.ItemsSource = _view;

        _autoPlay = service.AutoPlay;

        ApplyAutoPlay();

        RebuildView(clearSelection: false);
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    private async void OnAddClick(object? sender, RoutedEventArgs e)
    {
        if (_service is not { } service) return;

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

            service.AddRange(paths);
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

        _service?.AddRange(paths);
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

    private void OnSearchChanged(object? sender, TextChangedEventArgs e) => RebuildView(clearSelection: true);

    private void OnAutoPlayClick(object? sender, RoutedEventArgs e) => _service?.SetAutoPlay(!_autoPlay);

    private void OnAutoPlayStateChanged(bool autoPlay)
    {
        _autoPlay = autoPlay;

        ApplyAutoPlay();
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
        if (_service is not { } service) return;

        if (clearSelection)
            foreach (var entry in service.Entries) entry.MarkSelected(false);

        _view.Clear();

        foreach (var entry in service.Search(SearchInput.Text)) _view.Add(entry);

        if (_current is not null && !_view.Contains(_current)) SetCurrent(null);

        for (var index = 0; index < _view.Count; index++)
            _view[index].MarkMovable(index > 0, index < _view.Count - 1);

        RefreshState();
    }

    private void OnRevealClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: PlaylistEntry entry }) return;

        _service?.Reveal(entry.Path);
    }

    private void OnDeleteClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: PlaylistEntry entry }) return;

        _service?.Remove(entry.Path);
    }

    private void OnMoveUpClick(object? sender, RoutedEventArgs e) => MoveRow(sender, -1);

    private void OnMoveDownClick(object? sender, RoutedEventArgs e) => MoveRow(sender, 1);

    private void MoveRow(object? sender, int offset)
    {
        if (sender is not Button { DataContext: PlaylistEntry row }) return;
        if (_service is not { } service) return;

        var index = _view.IndexOf(row);
        var target = index + offset;

        if (index < 0 || target < 0 || target >= _view.Count) return;

        SetCurrent(row);

        service.Move(service.Entries.IndexOf(row), service.Entries.IndexOf(_view[target]));
    }

    private void OnDeleteSelectedClick(object? sender, RoutedEventArgs e)
    {
        if (_service is not { } service) return;

        var selected = service.Entries.Where(entry => entry.IsSelected).Select(entry => entry.Path).ToList();

        if (selected.Count == 0) return;

        service.RemoveRange(selected);
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
        var total = _service?.Entries.Count ?? 0;
        var selected = _service?.Entries.Count(entry => entry.IsSelected) ?? 0;

        CountText.Text = selected > 0
            ? $"{selected} of {total} {Count(total)}"
            : $"{total} {Count(total)}";

        DeleteSelectedButton.IsVisible = selected > 0;

        if (selected == 0 || total == 0) SelectAllBox.IsChecked = false;
        else if (selected == total) SelectAllBox.IsChecked = true;
        else SelectAllBox.IsChecked = null;
    }

    private static string Count(int total) => total == 1 ? "video" : "videos";

    private void OnRowPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Grid { DataContext: PlaylistEntry entry }) return;

        if (!e.GetCurrentPoint(null).Properties.IsLeftButtonPressed) return;

        SetCurrent(entry);
    }

    private void OnRowDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (e.Source is Visual source && source.FindAncestorOfType<Button>(true) is not null) return;
        if (sender is not Grid { DataContext: PlaylistEntry entry }) return;

        _service?.Play(entry.Path, VisiblePaths());
    }

    private void OnRowContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (sender is not Grid { DataContext: PlaylistEntry entry }) return;

        entry.MarkMissing(!File.Exists(entry.Path));

        RowMenu.Entry = entry;

        SetCurrent(entry);
    }

    private void SetCurrent(PlaylistEntry? entry)
    {
        if (ReferenceEquals(_current, entry)) return;

        _current?.MarkCurrent(false);

        _current = entry;

        _current?.MarkCurrent(true);
    }

    private void OnMenuPlayRequested(PlaylistEntry entry) => _service?.Play(entry.Path, VisiblePaths());

    private void OnMenuRevealRequested(PlaylistEntry entry) => _service?.Reveal(entry.Path);

    private void OnMenuDeleteRequested(PlaylistEntry entry) => _service?.Remove(entry.Path);
}
