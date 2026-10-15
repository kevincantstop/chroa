using System.Collections.ObjectModel;
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

    public void ShowEntries(ObservableCollection<PlaylistEntry> entries) => ItemHost.ItemsSource = entries;

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
