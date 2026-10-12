using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace ChroaVideoPlayer.Controls;

public partial class WindowActionButtons : UserControl
{
    public WindowActionButtons()
    {
        InitializeComponent();
    }

    private void OnMinimizeClick(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not Window window) return;

        window.WindowState = WindowState.Minimized;
    }

    private void OnMaximizeClick(object? sender, RoutedEventArgs e) => ToggleMaximize();

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not Window window) return;

        window.Close();
    }

    private void OnBarDoubleTapped(object? sender, TappedEventArgs e)
    {
        e.Handled = true;

        if (!ReferenceEquals(e.Source, sender)) return;

        ToggleMaximize();
    }

    private void ToggleMaximize()
    {
        if (TopLevel.GetTopLevel(this) is not Window window) return;

        window.WindowState = window.WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }
}