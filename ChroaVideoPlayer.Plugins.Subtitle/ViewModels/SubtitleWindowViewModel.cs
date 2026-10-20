using System;
using System.Collections.ObjectModel;
using Avalonia.Threading;
using ChroaVideoPlayer.Core.Mpv;
using ChroaVideoPlayer.Core.Plugin.Context;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ChroaVideoPlayer.Plugins.Subtitle;

public partial class SubtitleWindowViewModel : ObservableObject
{
    private ChroaMpvContext? _mpv;

    private int _currentId;

    public SubtitleWindowViewModel()
    {
        Tracks.CollectionChanged += (_, _) => OnPropertyChanged(nameof(IsEmpty));
    }

    public ObservableCollection<SubtitleTrackRow> Tracks { get; } = new();

    public bool IsEmpty => Tracks.Count == 0;

    [ObservableProperty]
    public partial bool CanAddSubtitle { get; set; }

    public IVideoOverlayHost? Overlay { get; private set; }

    public void Attach(ChroaMpvContext mpv, IVideoOverlayHost? overlay)
    {
        Detach();

        _mpv = mpv;
        Overlay = overlay;

        _mpv.SubtitlesChanged += OnSubtitlesChanged;
        _mpv.MediaOpened += OnMediaOpened;

        CanAddSubtitle = !string.IsNullOrEmpty(mpv.CurrentFile);

        ShowTracks(mpv.Subtitles);
    }

    public void Detach()
    {
        if (_mpv is not null)
        {
            _mpv.SubtitlesChanged -= OnSubtitlesChanged;
            _mpv.MediaOpened -= OnMediaOpened;
        }

        _mpv = null;
    }

    public void SetCurrent(SubtitleTrackRow? row)
    {
        _currentId = row?.Id ?? 0;

        ApplyCurrent();
    }

    public void Activate(SubtitleTrackRow row)
    {
        SetCurrent(row);

        _mpv?.SelectSubtitle(row.Id);
    }

    public void LoadSubtitle(string path) => _mpv?.LoadSubtitleFile(path);

    private void OnMediaOpened(object? sender, EventArgs e) =>
        Dispatcher.UIThread.Post(() => CanAddSubtitle = true);

    private void OnSubtitlesChanged(object? sender, SubtitleState state)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            ShowTracks(state);

            return;
        }

        Dispatcher.UIThread.Post(() => ShowTracks(state));
    }

    private void ShowTracks(SubtitleState state)
    {
        Tracks.Clear();

        foreach (var track in state.Tracks) Tracks.Add(new SubtitleTrackRow(track));

        ApplyCurrent();
    }

    private void ApplyCurrent()
    {
        foreach (var track in Tracks) track.IsCurrent = track.Id == _currentId;
    }
}
