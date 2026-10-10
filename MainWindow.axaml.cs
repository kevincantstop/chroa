using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using HanumanInstitute.LibMpv;

namespace ChroaVideoPlayer;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    // ==== 自己实现窗口拖动与双击最大化（不使用 WindowDecorationProperties.ElementRole）====
    private void OnChromePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // 按钮的 PointerPressed 会冒泡到这里；不挡掉的话按按钮会变成拖窗口
        if (e.Handled || !ReferenceEquals(e.Source, sender)) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        BeginMoveDrag(e);
    }

    private void OnChromeDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (!ReferenceEquals(e.Source, sender)) return;
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void OnMinimizeClick(object? sender, RoutedEventArgs e) =>
        WindowState = WindowState.Minimized;

    private void OnMaximizeClick(object? sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}