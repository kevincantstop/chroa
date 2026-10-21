using Avalonia.Controls;
using Avalonia.Interactivity;

namespace ChroaVideoPlayer.Plugins.Subtitle;

public partial class SubtitleGrid : UserControl
{
    public SubtitleGrid()
    {
        InitializeComponent();
    }

    private void OnSelectClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: SubtitleTrackRow row }) return;

        (DataContext as SubtitleWindowViewModel)?.Activate(row);
    }
}
