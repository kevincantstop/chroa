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

    public PlaylistPluginWindow()
    {
        InitializeComponent();
    }

    public event Action<IReadOnlyList<PlaylistEntry>>? DeleteRequested;

    public event Action<PlaylistEntry>? PlayRequested;

    public event Action<IReadOnlyList<string>>? AddRequested;

    public void ShowEntries(ObservableCollection<PlaylistEntry> entries)
    {
        if (_entries is not null) _entries.CollectionChanged -= OnEntriesChanged;

        _entries = entries;

        entries.CollectionChanged += OnEntriesChanged;

        ItemHost.ItemsSource = entries;

        RefreshState();
    }

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

            var paths = files
                .Select(file => file.TryGetLocalPath())
                .Where(path => !string.IsNullOrEmpty(path))
                .Select(path => path!)
                .ToList();

            if (paths.Count == 0) return;

            AddRequested?.Invoke(paths);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Add to playlist picker failed: {ex}");
        }
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
        if (_entries is not { } entries) return;

        var select = SelectAllBox.IsChecked == true;

        foreach (var entry in entries) entry.MarkSelected(select);

        RefreshState();
    }

    private void OnEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e) => RefreshState();

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

        PlayRequested?.Invoke(entry);
    }

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
