using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ChroaVideoPlayer.Core.Utils;
using ChroaVideoPlayer.Plugins.Playlist.Service;

namespace ChroaVideoPlayer.Plugins.Playlist;

public partial class PlaylistGrid : UserControl
{
    private PlaylistService? _service;

    private PlaylistEntry? _current;

    private PlaylistEntry? _anchor;

    private readonly ObservableCollection<PlaylistEntry> _view = new();

    private string? _filter;

    private bool _shiftSelection;

    private bool _rebuildPending;

    public static readonly DataFormat<IReadOnlyList<PlaylistEntry>> EntriesFormat =
        DataFormat.CreateInProcessFormat<IReadOnlyList<PlaylistEntry>>("chroa-playlist-entries");

    private const double DoubleClickMs = 500;

    private const double DoubleClickSlop = 8;

    private const double DragThreshold = 8;

    private PlaylistEntry? _lastPressedEntry;

    private DateTime _lastPressedUtc;

    private Point _lastPressedPoint;

    private PointerPressedEventArgs? _dragTrigger;

    private PlaylistEntry? _dragEntry;

    private Point _dragOrigin;

    private PlaylistEntry? _dropTarget;

    private bool _dropAfter;

    public PlaylistGrid()
    {
        InitializeComponent();

        AddHandler(PointerPressedEvent, OnCheckBoxPointerPressed, RoutingStrategies.Bubble, handledEventsToo: true);
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

        ItemHost.ItemsSource = _view;

        RebuildView(clearSelection: false);

        RefreshMissing(service);
    }

    private async void RefreshMissing(PlaylistService service)
    {
        var entries = service.Entries.ToList();

        var states = await Task.Run(() => entries
            .Select(entry => (Entry: entry, Missing: !File.Exists(entry.Path)))
            .ToList());

        foreach (var (entry, missing) in states) entry.MarkMissing(missing);
    }

    public void Filter(string? text)
    {
        _filter = text;

        RebuildView(clearSelection: true);
    }

    public async void RemoveSelected()
    {
        if (_service is not { } service) return;

        var selected = service.Entries.Where(entry => entry.IsSelected).Select(entry => entry.Path).ToList();

        if (selected.Count == 0) return;

        if (TopLevel.GetTopLevel(this) is not Window owner) return;

        var message = selected.Count == 1
            ? "Remove 1 video from the playlist?"
            : $"Remove {selected.Count} videos from the playlist?";

        if (!await ChroaDialogs.PromptAsync(owner, message, "Remove", "Remove", "Cancel")) return;

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

        _rebuildPending = false;
        _lastPressedEntry = null;

        if (clearSelection)
            foreach (var entry in service.Entries) entry.MarkSelected(false);

        _view.Clear();

        foreach (var entry in service.Search(_filter)) _view.Add(entry);

        if (_current is not null && !_view.Contains(_current)) SetCurrent(null);

        if (_anchor is not null && !_view.Contains(_anchor)) _anchor = null;

        for (var index = 0; index < _view.Count; index++)
            _view[index].MarkMovable(index > 0, index < _view.Count - 1);

        RefreshState();
    }

    private void RefreshState()
    {
        var total = TotalCount;
        var selected = SelectionCount;

        if (selected == 0) _anchor = null;

        SelectAllBox.IsChecked = selected == 0 || total == 0 ? false
            : selected == total ? true
            : null;

        SelectionChanged?.Invoke();
    }

    private List<string> VisiblePaths() => _view.Select(entry => entry.Path).ToList();

    private void OnEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_rebuildPending) return;

        _rebuildPending = true;

        Dispatcher.UIThread.Post(RefreshPending, DispatcherPriority.Background);
    }

    private void RefreshPending()
    {
        if (!_rebuildPending) return;

        RebuildView(clearSelection: false);
    }

    private void OnSelectAllClick(object? sender, RoutedEventArgs e)
    {
        var allSelected = _view.Count > 0 && _view.All(entry => entry.IsSelected);

        var select = !allSelected;

        foreach (var entry in _view) entry.MarkSelected(select);

        RefreshState();
    }

    private void OnRowCheckClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox { DataContext: PlaylistEntry entry } box) return;

        if (_shiftSelection)
        {
            _shiftSelection = false;

            box.IsChecked = entry.IsSelected;

            return;
        }

        SetCurrent(entry);

        var select = box.IsChecked == true;

        entry.MarkSelected(select);

        if (select) _anchor = entry;

        RefreshState();
    }

    private void OnRowPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Grid { DataContext: PlaylistEntry entry }) return;

        if (!e.GetCurrentPoint(null).Properties.IsLeftButtonPressed) return;

        var position = e.GetPosition(this);
        var now = DateTime.UtcNow;

        if (ReferenceEquals(_lastPressedEntry, entry) &&
            (now - _lastPressedUtc).TotalMilliseconds <= DoubleClickMs &&
            Math.Abs(position.X - _lastPressedPoint.X) <= DoubleClickSlop &&
            Math.Abs(position.Y - _lastPressedPoint.Y) <= DoubleClickSlop)
        {
            _lastPressedEntry = null;

            SetCurrent(entry);

            _service?.Play(entry.Path, VisiblePaths());

            return;
        }

        _lastPressedEntry = entry;
        _lastPressedUtc = now;
        _lastPressedPoint = position;

        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) ExtendSelection(entry);
        else if (e.KeyModifiers.HasFlag(KeyModifiers.Control)) ToggleSelection(entry);
        else SetCurrent(entry);

        _dragTrigger = e;
        _dragEntry = entry;
        _dragOrigin = position;
    }

    private void ToggleSelection(PlaylistEntry entry)
    {
        entry.MarkSelected(!entry.IsSelected);

        RefreshState();
    }

    private void OnCheckBoxPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.Handled) return;

        if (!e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            _shiftSelection = false;

            return;
        }

        if (e.Source is not Visual source) return;
        if (source.FindAncestorOfType<CheckBox>(true) is not { DataContext: PlaylistEntry entry }) return;

        _shiftSelection = true;

        ExtendSelection(entry);
    }

    private void ExtendSelection(PlaylistEntry entry)
    {
        if (_service is null) return;

        var anchor = _anchor ?? entry;

        _anchor = anchor;

        var from = _view.IndexOf(anchor);
        var to = _view.IndexOf(entry);

        if (to < 0) return;
        if (from < 0) from = to;
        if (from > to) (from, to) = (to, from);

        for (var index = 0; index < _view.Count; index++)
            _view[index].MarkSelected(index >= from && index <= to);

        RefreshState();
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

    private async void OnRowPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragTrigger is not { } trigger || _dragEntry is not { } entry) return;

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            ResetDrag();

            return;
        }

        var position = e.GetPosition(this);

        if (Math.Abs(position.X - _dragOrigin.X) < DragThreshold &&
            Math.Abs(position.Y - _dragOrigin.Y) < DragThreshold) return;

        ResetDrag();

        var data = new DataTransfer();

        data.Add(DataTransferItem.Create(EntriesFormat, DragSources(entry)));

        await DragDrop.DoDragDropAsync(trigger, data, DragDropEffects.Move);
    }

    private void OnRowDragOver(object? sender, DragEventArgs e)
    {
        if (sender is not Control row || row.DataContext is not PlaylistEntry target) return;

        if (!e.DataTransfer.Contains(EntriesFormat))
        {
            e.DragEffects = DragDropEffects.None;

            return;
        }

        e.DragEffects = DragDropEffects.Move;
        e.Handled = true;

        SetDropTarget(target, e.GetPosition(row).Y > row.Bounds.Height / 2);
    }

    private void OnRowDragLeave(object? sender, DragEventArgs e)
    {
        if (sender is not Control row || row.DataContext is not PlaylistEntry target) return;

        if (!ReferenceEquals(_dropTarget, target)) return;

        ClearDropTarget();
    }

    private void OnRowDrop(object? sender, DragEventArgs e)
    {
        if (sender is not Control row || row.DataContext is not PlaylistEntry target) return;
        if (e.DataTransfer.TryGetValue(EntriesFormat) is not { } sources) return;

        var after = e.GetPosition(row).Y > row.Bounds.Height / 2;

        ClearDropTarget();

        e.DragEffects = DragDropEffects.Move;
        e.Handled = true;

        MoveTo(sources, target, after);
    }

    private void MoveTo(IReadOnlyList<PlaylistEntry> sources, PlaylistEntry target, bool after)
    {
        if (_service is not { } service) return;

        var moving = sources.Where(service.Entries.Contains).ToList();

        if (moving.Count == 0) return;

        var remaining = service.Entries.Where(entry => !moving.Contains(entry)).ToList();
        var anchor = remaining.IndexOf(target);

        if (anchor < 0) return;

        service.MoveRange(moving, after ? anchor + 1 : anchor);
    }

    private IReadOnlyList<PlaylistEntry> DragSources(PlaylistEntry entry)
    {
        if (_service is not { } service) return [entry];

        var selected = service.Entries.Where(item => item.IsSelected).ToList();

        return selected.Count > 1 && selected.Contains(entry) ? selected : [entry];
    }

    private void SetDropTarget(PlaylistEntry target, bool after)
    {
        if (ReferenceEquals(_dropTarget, target) && _dropAfter == after) return;

        ClearDropTarget();

        _dropTarget = target;
        _dropAfter = after;

        target.MarkDrop(!after, after);
    }

    private void ClearDropTarget()
    {
        _dropTarget?.MarkDrop(false, false);

        _dropTarget = null;
        _dropAfter = false;
    }

    private void ResetDrag()
    {
        _dragTrigger = null;
        _dragEntry = null;
    }

    private void OnMenuPlayRequested(PlaylistEntry entry) => _service?.Play(entry.Path, VisiblePaths());

    private void OnMenuRevealRequested(PlaylistEntry entry) => _service?.Reveal(entry.Path);

    private void OnMenuDeleteRequested(PlaylistEntry entry) => _service?.Remove(entry.Path);
}
