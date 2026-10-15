using System;
using System.Collections.Generic;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using ChroaVideoPlayer.Core.Plugin;
using ChroaVideoPlayer.Core.Utils;
using ChroaVideoPlayer.ViewModels;

namespace ChroaVideoPlayer.Controls;

public partial class HomeMenu : UserControl
{
    private PluginWindowManager? _windows;

    public HomeMenu()
    {
        InitializeComponent();
    }

    private MainViewModel? Model => DataContext as MainViewModel;

    public void Initialize(PluginWindowManager windows, VideoPlayerViewModel player, IReadOnlyList<IChroaPlugin> plugins)
    {
        _windows = windows;

        foreach (var plugin in plugins) player.Plugins.Add(new PluginItem(plugin));
    }

    private void OnMenuButtonClick(object? sender, RoutedEventArgs e)
    {
        if (Model is not { } model) return;

        if (!model.Player.IsHomeMenuOpen) RefreshOpenState(model.Player);

        model.Player.ToggleHomeMenu();
    }

    private async void OnLoadVideoClick(object? sender, RoutedEventArgs e)
    {
        Model?.Player.CloseHomeMenu();

        try
        {
            var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
            if (storage is null) return;

            var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Load Video",
                AllowMultiple = false,
                FileTypeFilter = VideoFileTypes.Filter,
            });

            if (files.Count == 0) return;

            var path = files[0].TryGetLocalPath();
            if (string.IsNullOrEmpty(path)) return;

            Model?.Player.Play(path);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Open video picker failed: {ex}");
        }
    }

    private void OnPluginClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: PluginItem item }) return;

        _windows?.Toggle(item.Plugin);

        item.IsOpen = _windows?.IsOpen(item.Plugin) ?? false;

        Model?.Player.CloseHomeMenu();
    }

    private void RefreshOpenState(VideoPlayerViewModel player)
    {
        foreach (var item in player.Plugins) item.IsOpen = _windows?.IsOpen(item.Plugin) ?? false;
    }
}
