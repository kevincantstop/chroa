using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ChroaVideoPlayer.Core.Plugin.UI;

namespace ChroaVideoPlayer.Plugins.Playlist;

public partial class PlaylistPluginWindow : PluginWindow
{
    public PlaylistPluginWindow()
    {
        InitializeComponent();
    }

    public event Action<PlaylistEntry>? DeleteRequested;

    public void ShowEntries(ObservableCollection<PlaylistEntry> entries) => ItemHost.ItemsSource = entries;

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    private void OnRevealClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: PlaylistEntry entry }) return;

        Reveal(entry.Path);
    }

    private void OnDeleteClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: PlaylistEntry entry }) return;

        DeleteRequested?.Invoke(entry);
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
