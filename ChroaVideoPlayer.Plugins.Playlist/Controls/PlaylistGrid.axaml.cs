using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using ChroaVideoPlayer.Plugins.Playlist.Service;

namespace ChroaVideoPlayer.Plugins.Playlist;

public partial class PlaylistGrid : UserControl
{
    private PlaylistService? _service;

    private PlaylistEntry? _current;

    private readonly ObservableCollection<PlaylistEntry> _view = new();

    private string? _filter;

    public PlaylistGrid()
    {
        InitializeComponent();
    }

    public event Action? SelectionChanged;

    public int TotalCount => _service?.Entries.Count ?? 0;

    public int SelectionCount => _service?.Entries.Count(entry => entry.IsSelected) ?? 0;

    public void ShowService(PlaylistService service)
    {
        if (_service is not null) _service.Entries.CollectionChanged -= OnEntriesChanged;

        _service = service;

        service.EnsureLoaded();

        service.Entries.CollectionChanged += OnEntriesChanged;

        foreach (var entry in service.Entries) entry.MarkMissing(!File.Exists(entry.Path));

        ItemHost.ItemsSource = _view;

        RebuildView(clearSelection: false);
    }

    public void Filter(string? text)
    {
        _filter = text;

        RebuildView(clearSelection: true);
    }

    public void RemoveSelected()
    {
        if (_service is not { } service) return;

        var selected = service.Entries.Where(entry => entry.IsSelected).Select(entry => entry.Path).ToList();

        if (selected.Count == 0) return;

        service.RemoveRange(selected);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);

        if (_service is not null) _service.Entries.CollectionChanged -= OnEntriesChanged;
    }

    private void RebuildView(bool clearSelection)
    {
        if (_service is not { } service) return;

        if (clearSelection)
            foreach (var entry in service.Entries) entry.MarkSelected(false);

        _view.Clear();

        foreach (var entry in service.Search(_filter)) _view.Add(entry);

        if (_current is not null && !_view.Contains(_current)) SetCurrent(null);

        for (var index = 0; index < _view.Count; index++)
            _view[index].MarkMovable(index > 0, index < _view.Count - 1);

        RefreshState();
    }

    private void RefreshState()
    {
        var total = TotalCount;
        var selected = SelectionCount;

        SelectAllBox.IsChecked = selected == 0 || total == 0 ? false
            : selected == total ? true
            : null;

        SelectionChanged?.Invoke();
    }

    private List<string> VisiblePaths() => _view.Select(entry => entry.Path).ToList();

    private void OnEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e) => RebuildView(clearSelection: false);

    private void OnSelectAllClick(object? sender, RoutedEventArgs e)
    {
        var select = SelectAllBox.IsChecked == true;

        foreach (var entry in _view) entry.MarkSelected(select);

        RefreshState();
    }

    private void OnRowCheckClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox { DataContext: PlaylistEntry entry } box) return;

        entry.MarkSelected(box.IsChecked == true);

        RefreshState();
    }

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
