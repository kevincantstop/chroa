using Avalonia.Interactivity;
using ChroaVideoPlayer.Core.Plugin.UI;

namespace ChroaVideoPlayer.Plugins.Sample;

public partial class SamplePluginWindow : PluginWindow
{
    public SamplePluginWindow()
    {
        InitializeComponent();
    }

    public void ShowLastPlayed(string? path) =>
        LastPlayed.Text = string.IsNullOrEmpty(path) ? "Nothing played yet" : path;

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
