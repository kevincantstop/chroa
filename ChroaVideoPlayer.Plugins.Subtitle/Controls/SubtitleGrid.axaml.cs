using Avalonia.Controls;
using Avalonia.Input;

namespace ChroaVideoPlayer.Plugins.Subtitle;

public partial class SubtitleGrid : UserControl
{
    public SubtitleGrid()
    {
        InitializeComponent();
    }

    private void OnTrackPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: SubtitleTrackRow row }) return;

        (DataContext as SubtitleWindowViewModel)?.Select(row);
    }
}
