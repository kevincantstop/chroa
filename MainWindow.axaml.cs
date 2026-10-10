using System;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using ChroaVideoPlayer.ViewModels;

namespace ChroaVideoPlayer;

public partial class MainWindow : Window
{
    private MainViewModel? _viewModel;

    public MainWindow()
    {
        InitializeComponent();
    }

    // ==== 窗口自动适应视频宽高比（只读 ViewModel 的状态，不直接碰 mpv）====

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        // DataContext 可能被换掉，先把旧的解绑
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = DataContext as MainViewModel;

        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // VideoAspect 只在 UI 线程上被赋值（见 MainViewModel），所以这里不需要再切线程
        if (e.PropertyName == nameof(MainViewModel.VideoAspect) && _viewModel is not null)
        {
            ApplyVideoAspect(_viewModel.VideoAspect);
        }
    }

    private void ApplyVideoAspect(double aspect)
    {
        if (aspect <= 0 || double.IsNaN(aspect) || double.IsInfinity(aspect)) return;

        // 最大化 / 全屏时不调整
        if (WindowState != WindowState.Normal) return;

        // 宽度保持不变，按视频比例推高度
        var width = Width;
        var height = width / aspect;

        // 限制在当前屏幕的工作区内。
        // 用 this.Screens 而不是裸写 Screens：后者会和类型名 Avalonia.Controls.Screens 撞名，
        // 在继承链上遇到同名属性时会报 CS0176（之前 HorizontalAlignment 就是这么踩的）。
        // Screen.WorkingArea 是物理像素，而 Window.Width/Height 是 DIP，所以要除以显示缩放。
        var screen = this.Screens.ScreenFromWindow(this);
        if (screen is null)
        {
            screen = this.Screens.Primary;
        }

        if (screen is not null)
        {
            var scaling = RenderScaling > 0 ? RenderScaling : 1.0;
            var maxWidth = screen.WorkingArea.Width / scaling;
            var maxHeight = screen.WorkingArea.Height / scaling;

            var scale = Math.Min(1.0, Math.Min(maxWidth / width, maxHeight / height));
            width *= scale;
            height *= scale;
        }

        Width = Math.Round(width);
        Height = Math.Round(height);
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