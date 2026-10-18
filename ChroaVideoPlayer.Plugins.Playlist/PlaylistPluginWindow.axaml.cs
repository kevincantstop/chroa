using System;
using System.Collections.Generic;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using ChroaVideoPlayer.Core.Plugin.UI;
using ChroaVideoPlayer.Core.Utils;
using ChroaVideoPlayer.Plugins.Playlist.Service;

namespace ChroaVideoPlayer.Plugins.Playlist;

public partial class PlaylistPluginWindow : PluginWindow
{
    private PlaylistService? _service;

    private bool _autoPlay;

    public PlaylistPluginWindow()
    {
        InitializeComponent();

        RowList.SelectionChanged += RefreshState;
    }

    public void ShowService(PlaylistService service)
    {
        if (_service is not null) _service.AutoPlayChanged -= OnAutoPlayStateChanged;

        _service = service;

        service.AutoPlayChanged += OnAutoPlayStateChanged;

        RowList.ShowService(service);

        _autoPlay = service.AutoPlay;

        ApplyAutoPlay();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);

        if (_service is not null) _service.AutoPlayChanged -= OnAutoPlayStateChanged;
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
        if (e.DataTransfer.Contains(PlaylistGrid.EntryFormat)) return;

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

    private void OnSearchChanged(object? sender, TextChangedEventArgs e) => RowList.Filter(SearchInput.Text);

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

    private void OnDeleteSelectedClick(object? sender, RoutedEventArgs e) => RowList.RemoveSelected();

    private void RefreshState()
    {
        var total = RowList.TotalCount;
        var selected = RowList.SelectionCount;

        CountText.Text = selected > 0
            ? $"{selected} of {total} {Count(total)}"
            : $"{total} {Count(total)}";

        DeleteSelectedButton.IsVisible = selected > 0;
    }

    private static string Count(int total) => total == 1 ? "video" : "videos";
}
