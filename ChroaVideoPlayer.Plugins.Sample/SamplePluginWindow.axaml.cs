using Avalonia.Interactivity;
using ChroaVideoPlayer.Core.Plugin.UI;

namespace ChroaVideoPlayer.Plugins.Sample;

public partial class SamplePluginWindow : PluginWindow
{
    public SamplePluginWindow()
    {
        InitializeComponent();
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
