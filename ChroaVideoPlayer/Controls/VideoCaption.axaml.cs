using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using ChroaVideoPlayer.Mpv;
using ChroaVideoPlayer.ViewModels;

namespace ChroaVideoPlayer.Controls;

public partial class VideoCaption : UserControl
{
    public static readonly StyledProperty<ChroaMpvContext?> PlayerProperty =
        AvaloniaProperty.Register<VideoCaption, ChroaMpvContext?>(nameof(Player));

    private static readonly FilePickerFileType SubtitleFileType = new("Subtitle Files")
    {
        Patterns = new[] { "*.srt", "*.ass", "*.ssa", "*.sub", "*.vtt", "*.sup", "*.idx", "*.lrc" },
        MimeTypes = new[] { "text/*" },
        AppleUniformTypeIdentifiers = new[] { "public.text" },
    };

    private static readonly FilePickerFileType AllFileType = new("All Files")
    {
        Patterns = new[] { "*.*" },
    };

    private ChroaMpvContext? _attached;

    private VideoPlayerViewModel? ViewModel => DataContext as VideoPlayerViewModel;

    public VideoCaption()
    {
        InitializeComponent();
    }

    public ChroaMpvContext? Player
    {
        get => GetValue(PlayerProperty);
        set => SetValue(PlayerProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == PlayerProperty) AttachPlayer();
    }

    private void AttachPlayer()
    {
        var player = Player;
        if (ReferenceEquals(_attached, player)) return;

        if (_attached is not null) _attached.SubtitlesChanged -= OnSubtitlesChanged;

        _attached = player;

        if (_attached is not null) _attached.SubtitlesChanged += OnSubtitlesChanged;
    }

    private void OnSubtitlesChanged(object? sender, SubtitleState state) =>
        Dispatcher.UIThread.Post(() => ShowTracks(state));

    private void ShowTracks(SubtitleState state)
    {
        TrackHost.ItemsSource = state.Tracks;
        OffMark.IsVisible = !state.IsVisible;
        EmptyHint.IsVisible = state.Tracks.Count == 0;
    }

    private void OnOffClick(object? sender, RoutedEventArgs e) => Select(0);

    private void OnTrackClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: SubtitleTrackInfo track }) return;

        Select(track.Id);
    }

    private void Select(int id)
    {
        ViewModel?.CloseSubtitleMenu();

        Player?.SelectSubtitle(id);
    }

    private async void OnLoadClick(object? sender, RoutedEventArgs e)
    {
        ViewModel?.CloseSubtitleMenu();

        try
        {
            var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
            if (storage is null) return;

            var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Load Subtitle",
                AllowMultiple = false,
                FileTypeFilter = [SubtitleFileType, AllFileType],
            });

            if (files.Count == 0) return;

            var path = files[0].TryGetLocalPath();
            if (string.IsNullOrEmpty(path)) return;

            Player?.LoadSubtitleFile(path);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Open subtitle picker failed: {ex}");
        }
    }
}
