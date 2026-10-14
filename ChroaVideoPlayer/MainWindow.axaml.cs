using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ChroaVideoPlayer.Controls;
using ChroaVideoPlayer.Core.Persistance;
using ChroaVideoPlayer.Core.Utils;
using ChroaVideoPlayer.Settings;
using ChroaVideoPlayer.ViewModels;
using Avalonia.Platform.Storage;

namespace ChroaVideoPlayer;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        DataContext = new MainViewModel(VideoPlayer.ViewModel);

        VideoPlayer.ViewModel.PropertyChanged += OnPlayerPropertyChanged;
        VideoPlayer.FullScreenToggleRequested += ToggleFullScreen;
        VideoPlayer.TopMostToggleRequested += ToggleTopMost;

        _idleTimer.Tick += OnIdleTimerTick;

        PropertyChanged += OnWindowPropertyChanged;
    }


    private const string SettingsKey = "default";

    private const double MaxWorkingAreaFraction = 0.8;

    private const int FrameSlackPixels = 8;

    private readonly DispatcherTimer _idleTimer = new() { Interval = TimeSpan.FromSeconds(3) };

    private readonly LocalStore<VideoPlayerSettings> _settingsStore = new();

    private WindowState _stateBeforeFullScreen = WindowState.Normal;

    private bool _isChromeIdle;

    private VideoPlayerSettings _settings = new();

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        WindowUtils.RemoveSystemBorder(this);

        ClampToMaxWorkingArea();

        LoadSettings();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        WakeChrome();

        if (HandleFullScreenKey(e)) return;

        base.OnKeyDown(e);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        WakeChrome();
    }

    private void OnPlayerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(VideoPlayerViewModel.VideoAspect))
        {
            ApplyVideoAspectForCurrentFile(VideoPlayer.ViewModel.VideoAspect);
        }
        else if (e.PropertyName == nameof(VideoPlayerViewModel.IsDragging))
        {
            RootGrid.Classes.Set("dragging", VideoPlayer.ViewModel.IsDragging);
        }
        else if (e.PropertyName == nameof(VideoPlayerViewModel.Volume))
        {
            QueueSettingsSave();
        }
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

    private bool ApplyVideoAspect(double aspect)
    {
        if (aspect <= 0 || double.IsNaN(aspect) || double.IsInfinity(aspect)) return false;

        if (WindowState != WindowState.Normal) return false;

        if (GetMaxWindowSize() is not { } max) return false;

        var width = Width;
        var height = width / aspect;
        var scale = Math.Min(1.0, Math.Min(max.Width / width, max.Height / height));

        ApplyWindowSize(width * scale, height * scale);

        return true;
    }

    private void ApplyVideoAspectForCurrentFile(double aspect)
    {
        var path = VideoPlayer.ViewModel.Mpv.CurrentFile ?? string.Empty;

        if (path.Length > 0 &&
            string.Equals(path, _settings.LastVideoPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!ApplyVideoAspect(aspect)) return;

        _settings.LastVideoPath = path;

        QueueSettingsSave();
    }

    private bool HandleFullScreenKey(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (WindowState != WindowState.FullScreen) return false;

            SetFullScreen(false);
        }
        else if (e.Key == Key.F11 || (e.Key == Key.Enter && (e.KeyModifiers & KeyModifiers.Alt) != 0))
        {
            SetFullScreen(WindowState != WindowState.FullScreen);
        }
        else
        {
            return false;
        }

        e.Handled = true;
        return true;
    }

    private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == WindowStateProperty) ApplyFullScreenState();
        else if (e.Property == WidthProperty || e.Property == HeightProperty) QueueSettingsSave();
    }

    private void ToggleFullScreen() => SetFullScreen(WindowState != WindowState.FullScreen);

    private void ToggleTopMost()
    {
        Topmost = !Topmost;

        VideoPlayer.IsTopMost = Topmost;

        QueueSettingsSave();
    }

    private void SetFullScreen(bool fullScreen)
    {
        if (fullScreen)
        {
            if (WindowState == WindowState.FullScreen) return;

            _stateBeforeFullScreen = WindowState;

            WindowState = WindowState.FullScreen;

            return;
        }

        if (WindowState != WindowState.FullScreen) return;

        WindowState = _stateBeforeFullScreen;
    }

    private void ApplyFullScreenState()
    {
        var fullScreen = WindowState == WindowState.FullScreen;

        WindowButtons.IsVisible = !fullScreen;
        Resizer.IsVisible = !fullScreen;
        VideoPlayer.IsFullScreen = fullScreen;

        _idleTimer.Stop();

        SetChromeIdle(false);

        if (fullScreen) _idleTimer.Start();
    }

    private void OnIdleTimerTick(object? sender, EventArgs e)
    {
        _idleTimer.Stop();

        SetChromeIdle(true);
    }

    private void WakeChrome()
    {
        if (WindowState != WindowState.FullScreen) return;

        SetChromeIdle(false);

        _idleTimer.Stop();
        _idleTimer.Start();
    }

    private void SetChromeIdle(bool idle)
    {
        if (_isChromeIdle == idle) return;

        _isChromeIdle = idle;

        if (idle) VideoPlayer.ViewModel.CloseMenus();

        RootGrid.Classes.Set("idle", idle);
        IdleLayer.IsVisible = idle;
    }


    private void OnRootPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        WakeChrome();

        if (e.Handled) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if (WindowState == WindowState.FullScreen) return;

        if (e.Source is Visual source &&
            (source.FindAncestorOfType<Button>(true) is not null
             || source.FindAncestorOfType<DragBar>(true) is not null))
        {
            return;
        }

        BeginMoveDrag(e);
    }

    private void OnRootPointerExited(object? sender, PointerEventArgs e) =>
        VideoPlayer.ViewModel.CloseMenus();

    private void OnRootDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (e.Handled) return;

        if (e.Source is Visual source &&
            (source.FindAncestorOfType<Button>(true) is not null
             || source.FindAncestorOfType<DragBar>(true) is not null))
        {
            return;
        }

        ToggleFullScreen();
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

        VideoPlayer.Play(path);
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


    private void LoadSettings()
    {
        _settings = _settingsStore.Get(SettingsKey) ?? new VideoPlayerSettings();

        VideoPlayer.ViewModel.SetVolume(_settings.Volume);

        Topmost = _settings.AlwaysOnTop;
        VideoPlayer.IsTopMost = Topmost;

        ApplySavedWindowSize();
    }

    private void ApplySavedWindowSize()
    {
        if (_settings.WindowWidth <= 0 || _settings.WindowHeight <= 0) return;

        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (screen is null) return;

        var scaling = screen.Scaling > 0 ? screen.Scaling : 1.0;
        var maxWidth = screen.WorkingArea.Width / scaling;
        var maxHeight = screen.WorkingArea.Height / scaling;
        var scale = Math.Min(1.0, Math.Min(maxWidth / _settings.WindowWidth, maxHeight / _settings.WindowHeight));

        ApplyWindowSize(_settings.WindowWidth * scale, _settings.WindowHeight * scale);
    }

    private void QueueSettingsSave()
    {
        UpdateSettingsFromUi();

        _settingsStore.QueueSave(SettingsKey, _settings);
    }

    private void UpdateSettingsFromUi()
    {
        _settings.Volume = VideoPlayer.ViewModel.Volume;
        _settings.AlwaysOnTop = Topmost;

        if (WindowState != WindowState.Normal) return;

        _settings.WindowWidth = Width;
        _settings.WindowHeight = Height;
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        _settingsStore.Flush();

        base.OnClosing(e);
    }
}
