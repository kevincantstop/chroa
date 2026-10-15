using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using ChroaVideoPlayer.Core.Persistance;
using ChroaVideoPlayer.Core.Utils;
using ChroaVideoPlayer.ViewModels;

namespace ChroaVideoPlayer.Settings;

public sealed class VideoPlayerSettingsBinder
{
    public const int DefaultRecentVideoLimit = 30;

    private readonly LocalStore<VideoPlayerSettings> _store;

    private VideoPlayerSettings _row = new();

    private Window? _window;

    private VideoPlayerViewModel? _viewModel;

    private string? _path;

    private bool _isNew;

    private bool _isApplying;

    public VideoPlayerSettingsBinder(int recentVideoLimit = DefaultRecentVideoLimit) =>
        _store = new LocalStore<VideoPlayerSettings>(recentVideoLimit, nameof(VideoPlayerSettings.LastOpenedUtc));

    public void Attach(Window window, VideoPlayerViewModel viewModel)
    {
        Detach();

        _window = window;
        _viewModel = viewModel;

        window.PropertyChanged += OnWindowPropertyChanged;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        viewModel.Mpv.MediaOpened += OnMediaOpened;
    }

    public void Detach()
    {
        if (_window is not null) _window.PropertyChanged -= OnWindowPropertyChanged;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _viewModel.Mpv.MediaOpened -= OnMediaOpened;
        }

        _window = null;
        _viewModel = null;
        _path = null;

        _store.Flush();
    }

    public bool HasSettingsFor(string path) => _store.Contains(path);

    private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == WindowBase.TopmostProperty ||
            e.Property == Window.WidthProperty ||
            e.Property == Window.HeightProperty)
        {
            SaveCurrent();
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(VideoPlayerViewModel.Volume)) return;

        SaveCurrent();
    }

    private void OnMediaOpened(object? sender, EventArgs e) =>
        Dispatcher.UIThread.Post(ApplyCurrentVideo);

    private void ApplyCurrentVideo()
    {
        if (_window is null || _viewModel is null) return;

        var path = _viewModel.Mpv.CurrentFile;
        if (string.IsNullOrEmpty(path)) return;

        Track(path);

        if (_isNew) return;

        var volume = _row.Volume;
        var alwaysOnTop = _row.AlwaysOnTop;
        var width = _row.WindowWidth;
        var height = _row.WindowHeight;

        _isApplying = true;

        try
        {
            _viewModel.SetVolume(volume);

            _window.Topmost = alwaysOnTop;

            if (_window.WindowState == WindowState.Normal)
            {
                WindowUtils.FitInsideWorkingArea(_window, width, height);
            }
        }
        finally
        {
            _isApplying = false;
        }

        SaveCurrent();
    }

    private void SaveCurrent()
    {
        if (_isApplying) return;

        if (_window is null || _viewModel is null) return;

        var path = _viewModel.Mpv.CurrentFile;
        if (string.IsNullOrEmpty(path)) return;

        Track(path);

        _row.Volume = _viewModel.Volume;
        _row.AlwaysOnTop = _window.Topmost;

        if (_window.WindowState == WindowState.Normal)
        {
            _row.WindowWidth = _window.Width;
            _row.WindowHeight = _window.Height;
        }

        _row.LastOpenedUtc = DateTime.UtcNow;

        if (!_isNew)
        {
            _store.QueueSave(path, _row);

            return;
        }

        _store.Save(path, _row);

        _isNew = false;
    }

    private void Track(string path)
    {
        if (string.Equals(path, _path, StringComparison.Ordinal)) return;

        _path = path;

        var stored = _store.Get(path);

        _isNew = stored is null;

        _row = stored ?? new VideoPlayerSettings();
    }
}
