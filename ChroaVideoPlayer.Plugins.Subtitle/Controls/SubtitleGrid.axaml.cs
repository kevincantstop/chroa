using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

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

        (DataContext as SubtitleWindowViewModel)?.SetCurrent(row);
    }

    private void OnAddClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: SubtitleTrackRow row }) return;

        (DataContext as SubtitleWindowViewModel)?.Activate(row);
    }
}
