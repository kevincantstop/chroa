using System;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ChroaVideoPlayer.Controls;
using ChroaVideoPlayer.Core.EventBus;
using ChroaVideoPlayer.Core.EventBus.Events;
using ChroaVideoPlayer.Core.Plugin;
using ChroaVideoPlayer.Core.Plugin.Context;
using ChroaVideoPlayer.Core.Plugin.UI;
using ChroaVideoPlayer.Core.UI;
using ChroaVideoPlayer.Core.Utils;
using ChroaVideoPlayer.Settings;
using ChroaVideoPlayer.ViewModels;
using Avalonia.Platform.Storage;

namespace ChroaVideoPlayer;

public partial class MainWindow : ChroaWindow
{
    public MainWindow()
    {
        InitializeComponent();

        DataContext = new MainViewModel(VideoPlayer.ViewModel);

        _pluginWindows = new PluginWindowManager(this);

        _eventBus = new UIEventBus(new EventBus());

        _application = new ChroaApplicationContext(
            VideoPlayer.ViewModel.Mpv,
            new VideoOverlayHost(RootGrid, VideoPlayer));

        VideoPlayer.Events = _eventBus;

        var plugins = PluginScanner.Load().Where(plugin => plugin.Enabled).ToList();

        foreach (var plugin in plugins) plugin.Context = _application;

        foreach (var plugin in plugins) (plugin as IChroaPluginEvents)?.SubscribeEvents(_eventBus);

        HomeMenu.Initialize(_pluginWindows, VideoPlayer.ViewModel, plugins);

        VideoPlayer.ViewModel.PropertyChanged += OnPlayerPropertyChanged;
        VideoPlayer.FullScreenToggleRequested += ToggleFullScreen;
        VideoPlayer.TopMostToggleRequested += ToggleTopMost;

        _idleTimer.Tick += OnIdleTimerTick;

        PropertyChanged += OnWindowPropertyChanged;

        AddHandler(PointerPressedEvent, OnWindowPointerPressed, RoutingStrategies.Bubble, handledEventsToo: true);
    }


    private const double MaxWorkingAreaFraction = 0.8;

    private readonly DispatcherTimer _idleTimer = new() { Interval = TimeSpan.FromSeconds(3) };

    private readonly VideoPlayerSettingsBinder _settings = new();

    private readonly PluginWindowManager _pluginWindows;

    private readonly IEventBus _eventBus;

    private readonly ChroaApplicationContext _application;

    private WindowState _stateBeforeFullScreen = WindowState.Normal;

    private bool _isChromeIdle;

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        WindowUtils.ClampToWorkingArea(this, MaxWorkingAreaFraction);

        _settings.Attach(this, VideoPlayer.ViewModel);
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        _pluginWindows.CloseAll();

        _settings.Detach();

        VideoPlayer.ClearPreviewCache();

        base.OnClosing(e);
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

    private void OnPreviousVideoClick(object? sender, RoutedEventArgs e) =>
        _eventBus.Publish(new VideoStepRequested(-1));

    private void OnNextVideoClick(object? sender, RoutedEventArgs e) =>
        _eventBus.Publish(new VideoStepRequested(1));

    private void OnPlayerPropertyChanged(object? sender, PropertyChangedEventArgs e)    {
        if (e.PropertyName == nameof(VideoPlayerViewModel.VideoAspect))
        {
            ApplyVideoAspectForCurrentFile(VideoPlayer.ViewModel.VideoAspect);
        }
        else if (e.PropertyName == nameof(VideoPlayerViewModel.IsDragging))
        {
            RootGrid.Classes.Set("dragging", VideoPlayer.ViewModel.IsDragging);
        }
    }

    private void ApplyVideoAspectForCurrentFile(double aspect)
    {
        if (VideoPlayer.ViewModel.IsFreeResize) return;

        WindowUtils.FitToAspect(this, aspect, MaxWorkingAreaFraction, WindowUtils.PanelShadowMargin);
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
        if (e.Property == WindowStateProperty) ApplyWindowState();
        else if (e.Property == WindowBase.TopmostProperty) VideoPlayer.IsTopMost = Topmost;
    }

    private void ToggleFullScreen() => SetFullScreen(WindowState != WindowState.FullScreen);

    private void ToggleTopMost() => Topmost = !Topmost;

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

    private void ApplyWindowState()
    {
        var fullScreen = WindowState == WindowState.FullScreen;
        var maximized = WindowState == WindowState.Maximized;

        IsPanelFlush = fullScreen || maximized;

        WindowButtons.IsVisible = !fullScreen;
        Resizer.IsVisible = !fullScreen && !maximized;
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


    private void OnWindowPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is Visual source && source.FindAncestorOfType<HomeMenu>(true) is not null) return;

        VideoPlayer.ViewModel.CloseHomeMenu();
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

        if (SubtitleFileTypes.IsSubtitleFile(path))
        {
            if (VideoPlayer.ViewModel.HasVideo) VideoPlayer.ViewModel.Mpv.LoadSubtitleFile(path);

            return;
        }

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
}
