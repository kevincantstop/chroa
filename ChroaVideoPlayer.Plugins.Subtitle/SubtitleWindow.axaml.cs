using Avalonia.Interactivity;
using ChroaVideoPlayer.Core.Plugin.UI;

namespace ChroaVideoPlayer.Plugins.Subtitle;

public partial class SubtitleWindow : PluginWindow
{
    public SubtitleWindow()
    {
        InitializeComponent();
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
