using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using ChroaVideoPlayer.Core.Mpv;
using ChroaVideoPlayer.Core.Plugin.UI;

namespace ChroaVideoPlayer.Plugins.Subtitle;

public partial class SubtitleWindow : PluginWindow
{
    private readonly ObservableCollection<SubtitleTrackRow> _tracks = new();

    private ChroaMpvContext? _mpv;

    public SubtitleWindow()
    {
        InitializeComponent();

        TrackList.ItemsSource = _tracks;
    }

    public void Attach(ChroaMpvContext mpv)
    {
        Detach();

        _mpv = mpv;

        _mpv.SubtitlesChanged += OnSubtitlesChanged;

        ShowTracks(mpv.Subtitles);
    }

    public void Detach()
    {
        if (_mpv is not null) _mpv.SubtitlesChanged -= OnSubtitlesChanged;

        _mpv = null;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);

        Detach();
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    private void OnSubtitlesChanged(object? sender, SubtitleState state) => ShowTracks(state);

    private void ShowTracks(SubtitleState state)
    {
        _tracks.Clear();

        foreach (var track in state.Tracks) _tracks.Add(new SubtitleTrackRow(track));

        EmptyHint.IsVisible = _tracks.Count == 0;
    }

    private void OnTrackPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: SubtitleTrackRow row }) return;

        _mpv?.SelectSubtitle(row.Id);
    }
}
