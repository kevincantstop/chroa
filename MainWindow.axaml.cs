using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using ChroaVideoPlayer.Controls;
using ChroaVideoPlayer.Utils;
using ChroaVideoPlayer.ViewModels;
using Avalonia.Platform.Storage;

namespace ChroaVideoPlayer;

public partial class MainWindow : Window
{
    private MainViewModel? _viewModel;

    public MainWindow()
    {
        InitializeComponent();

        SeekBar.CanDrag = () => _viewModel?.CanSeek == true;
        SeekBar.Started += () => _viewModel?.BeginSeek();
        SeekBar.Dragged += fraction => _viewModel?.UpdateSeekFraction(fraction);
        SeekBar.Committed += _ => _viewModel?.CommitSeek();

        VolumeBar.CanDrag = () => _viewModel is { HasVideo: true, CanAdjustVolume: true };
        VolumeBar.Dragged += fraction => _viewModel?.UpdateVolumeFraction(fraction);

        KeepChromeVisibleWhileDragging(SeekBar);
        KeepChromeVisibleWhileDragging(VolumeBar);
    }


    private const double MaxWorkingAreaFraction = 0.8;

    private const int FrameSlackPixels = 8;

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        WindowUtils.RemoveSystemBorder(this);

        ClampToMaxWorkingArea();
    }

    private (double Width, double Height)? GetMaxWindowSize()
    {
        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (screen is null) return null;

        var scaling = screen.Scaling > 0 ? screen.Scaling : 1.0;
        return (screen.WorkingArea.Width / scaling * MaxWorkingAreaFraction,
                screen.WorkingArea.Height / scaling * MaxWorkingAreaFraction);
    }

    private void ApplyWindowSize(double width, double height)
    {
        Width = Math.Round(width);
        Height = Math.Round(height);

        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (screen is null) return;

        var scaling = screen.Scaling > 0 ? screen.Scaling : 1.0;
        var work = screen.WorkingArea;
        var pixelWidth = (int)Math.Round(Width * scaling) + FrameSlackPixels;
        var pixelHeight = (int)Math.Round(Height * scaling) + FrameSlackPixels;
        var pos = Position;

        var x = Math.Clamp(pos.X, work.X, Math.Max(work.X, work.Right - pixelWidth));
        var y = Math.Clamp(pos.Y, work.Y, Math.Max(work.Y, work.Bottom - pixelHeight));
        if (x != pos.X || y != pos.Y) Position = new PixelPoint(x, y);
    }

    private void ClampToMaxWorkingArea()
    {
        if (GetMaxWindowSize() is not { } max) return;

        var scale = Math.Min(1.0, Math.Min(max.Width / Width, max.Height / Height));
        if (scale >= 1.0) return;

        ApplyWindowSize(Width * scale, Height * scale);
    }


    private void OnRootPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Handled) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        if (e.Source is Visual source &&
            (source.FindAncestorOfType<Button>(true) is not null
             || IsInside(source, SeekBar)
             || IsInside(source, VolumeBar)))
        {
            return;
        }

        BeginMoveDrag(e);
    }


    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = DataContext as MainViewModel;

        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;

            SeekBar.SetFraction(_viewModel.SeekFraction);
            VolumeBar.SetFraction(_viewModel.VolumeFraction);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_viewModel is null) return;

        if (e.PropertyName == nameof(MainViewModel.VideoAspect))
        {
            ApplyVideoAspect(_viewModel.VideoAspect);
        }
        else if (e.PropertyName == nameof(MainViewModel.SeekFraction))
        {
            SeekBar.SetFraction(_viewModel.SeekFraction);
        }
        else if (e.PropertyName == nameof(MainViewModel.VolumeFraction))
        {
            VolumeBar.SetFraction(_viewModel.VolumeFraction);
        }
    }

    private void ApplyVideoAspect(double aspect)
    {
        if (aspect <= 0 || double.IsNaN(aspect) || double.IsInfinity(aspect)) return;

        if (WindowState != WindowState.Normal) return;

        if (GetMaxWindowSize() is not { } max) return;

        var width = Width;
        var height = width / aspect;
        var scale = Math.Min(1.0, Math.Min(max.Width / width, max.Height / height));

        ApplyWindowSize(width * scale, height * scale);
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = HasDroppedFile(e) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        e.Handled = true;

        var path = GetFirstDroppedFilePath(e);
        if (path is null) return;

        _viewModel?.Mpv.Play(path);
    }

    private static bool HasDroppedFile(DragEventArgs e)
    {
        var files = e.DataTransfer.TryGetFiles();
        return files is { Length: > 0 };
    }

    private static string? GetFirstDroppedFilePath(DragEventArgs e)
    {
        var files = e.DataTransfer.TryGetFiles();
        if (files is null) return null;

        foreach (var item in files)
        {
            if (item is not IStorageFile) continue;

            var path = item.TryGetLocalPath();
            if (!string.IsNullOrEmpty(path)) return path;
        }

        return null;
    }


    private void KeepChromeVisibleWhileDragging(DragBar bar)
    {
        bar.Started += () => RootGrid.Classes.Set("dragging", true);
        bar.Committed += _ => RootGrid.Classes.Set("dragging", false);
    }

    private static bool IsInside(Visual source, Visual target)
    {
        for (Visual? v = source; v is not null; v = v.GetVisualParent())
        {
            if (ReferenceEquals(v, target)) return true;
        }

        return false;
    }


    private void OnPlayPauseClick(object? sender, RoutedEventArgs e) => _viewModel?.Mpv.TogglePause();

    private void OnMuteClick(object? sender, RoutedEventArgs e) => _viewModel?.Mpv.ToggleMute();
}