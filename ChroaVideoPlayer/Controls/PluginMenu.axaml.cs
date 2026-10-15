using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ChroaVideoPlayer.Core.Plugin;
using ChroaVideoPlayer.ViewModels;

namespace ChroaVideoPlayer.Controls;

public partial class PluginMenu : UserControl
{
    private PluginWindowManager? _windows;

    public PluginMenu()
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

        if (!model.Player.IsPluginMenuOpen) RefreshOpenState(model.Player);

        model.Player.TogglePluginMenu();
    }

    private void OnPluginClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: PluginItem item }) return;

        _windows?.Toggle(item.Plugin);

        item.IsOpen = _windows?.IsOpen(item.Plugin) ?? false;

        Model?.Player.ClosePluginMenu();
    }

    private void RefreshOpenState(VideoPlayerViewModel player)
    {
        foreach (var item in player.Plugins) item.IsOpen = _windows?.IsOpen(item.Plugin) ?? false;
    }
}
