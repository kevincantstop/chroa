using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ChroaVideoPlayer.ViewModels;

namespace ChroaVideoPlayer.Controls;

public partial class PlaybackSpeed : UserControl
{
    public PlaybackSpeed()
    {
        InitializeComponent();
    }

    public void ApplyState(double speed)
    {
        NormalMark.IsVisible = IsSelected(speed, 1);
        SlowMark.IsVisible = IsSelected(speed, 1.2);
        MediumMark.IsVisible = IsSelected(speed, 1.5);
        FastMark.IsVisible = IsSelected(speed, 2);
        FastestMark.IsVisible = IsSelected(speed, 3);
    }

    private VideoPlayerViewModel? ViewModel => DataContext as VideoPlayerViewModel;

    private static bool IsSelected(double speed, double target) => Math.Abs(speed - target) < 0.001;

    private void OnNormalClick(object? sender, RoutedEventArgs e) => Select(1);

    private void OnSlowClick(object? sender, RoutedEventArgs e) => Select(1.2);

    private void OnMediumClick(object? sender, RoutedEventArgs e) => Select(1.5);

    private void OnFastClick(object? sender, RoutedEventArgs e) => Select(2);

    private void OnFastestClick(object? sender, RoutedEventArgs e) => Select(3);

    private void Select(double speed)
    {
        ViewModel?.CloseSpeedMenu();

        ViewModel?.SetSpeed(speed);
    }
}
