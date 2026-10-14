using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using ChroaVideoPlayer.Core.Persistance;
using ChroaVideoPlayer.Core.Utils;
using ChroaVideoPlayer.ViewModels;

namespace ChroaVideoPlayer.Settings;

public sealed class VideoPlayerSettings
{
    private const string Key = "default";

    private readonly LocalStore<VideoPlayerSettings> _store = new();

    private Window? _window;

    private VideoPlayerViewModel? _viewModel;

    public double Volume { get; set; } = 100;

    public bool AlwaysOnTop { get; set; }

    public double WindowWidth { get; set; }

    public double WindowHeight { get; set; }

    public string LastVideoPath { get; set; } = string.Empty;

    public void Attach(Window window, VideoPlayerViewModel viewModel)
    {
        Detach();

        _window = window;
        _viewModel = viewModel;

        Restore();

        window.PropertyChanged += OnWindowPropertyChanged;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    public void Detach()
    {
        if (_window is not null) _window.PropertyChanged -= OnWindowPropertyChanged;
        if (_viewModel is not null) _viewModel.PropertyChanged -= OnViewModelPropertyChanged;

        _window = null;
        _viewModel = null;

        _store.Flush();
    }

    public bool TryRestoreWindowSize(string path)
    {
        if (_window is null) return false;

        if (!string.Equals(path, LastVideoPath, StringComparison.OrdinalIgnoreCase)) return false;

        if (_window.WindowState != WindowState.Normal) return false;

        return WindowUtils.FitInsideWorkingArea(_window, WindowWidth, WindowHeight);
    }

    private void Restore()
    {
        if (_window is null || _viewModel is null) return;

        if (_store.Get(Key) is { } stored) CopyFrom(stored);

        _viewModel.SetVolume(Volume);

        _window.Topmost = AlwaysOnTop;
    }

    private void CopyFrom(VideoPlayerSettings source)
    {
        Volume = source.Volume;
        AlwaysOnTop = source.AlwaysOnTop;
        WindowWidth = source.WindowWidth;
        WindowHeight = source.WindowHeight;
        LastVideoPath = source.LastVideoPath;
    }

    private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == WindowBase.TopmostProperty)
        {
            AlwaysOnTop = _window!.Topmost;

            QueueSave();
        }
        else if (e.Property == Window.WidthProperty || e.Property == Window.HeightProperty)
        {
            SaveWindowSize();
        }
    }

    private void SaveWindowSize()
    {
        if (_window!.WindowState != WindowState.Normal) return;

        var path = _viewModel!.Mpv.CurrentFile;
        if (string.IsNullOrEmpty(path)) return;

        LastVideoPath = path;

        WindowWidth = _window.Width;
        WindowHeight = _window.Height;

        QueueSave();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(VideoPlayerViewModel.Volume)) return;

        Volume = _viewModel!.Volume;

        QueueSave();
    }

    private void QueueSave() => _store.QueueSave(Key, this);
}
