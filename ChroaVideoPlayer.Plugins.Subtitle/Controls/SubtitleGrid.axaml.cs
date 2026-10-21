using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using ChroaVideoPlayer.Core.Utils;

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
        if (DataContext is not SubtitleWindowViewModel viewModel) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        viewModel.SetCurrent(row);
    }

    private void OnAddClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: SubtitleTrackRow row }) return;

        (DataContext as SubtitleWindowViewModel)?.Activate(row);
    }

    private async void OnFrameClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: SubtitleTrackRow row }) return;
        if (DataContext is not SubtitleWindowViewModel viewModel) return;

        if (viewModel.IsFrameVisible)
        {
            viewModel.IsFrameVisible = false;

            return;
        }

        if (await viewModel.ShowMovementAsync(row) is not { } message) return;
        if (TopLevel.GetTopLevel(this) is not Window owner) return;

        await ChroaDialogs.AlertAsync(owner, message, "Subtitle");
    }
}
