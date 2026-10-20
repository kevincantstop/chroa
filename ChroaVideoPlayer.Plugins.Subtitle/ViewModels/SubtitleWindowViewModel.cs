using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Threading;
using ChroaVideoPlayer.Core.Mpv;
using ChroaVideoPlayer.Core.Plugin.Context;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ChroaVideoPlayer.Plugins.Subtitle;

public partial class SubtitleWindowViewModel : ObservableObject
{
    private readonly Dictionary<int, (double X, double Y)> _positions = new();

    private ChroaMpvContext? _mpv;

    private int _currentId;

    private double _aspect = 16.0 / 9.0;

    public SubtitleWindowViewModel()
    {
        Tracks.CollectionChanged += (_, _) => OnPropertyChanged(nameof(IsEmpty));
    }

    public ObservableCollection<SubtitleTrackRow> Tracks { get; } = new();

    public bool IsEmpty => Tracks.Count == 0;

    [ObservableProperty]
    public partial bool CanAddSubtitle { get; set; }

    [ObservableProperty]
    public partial bool IsFrameVisible { get; set; }

    public IVideoOverlayHost? Overlay { get; private set; }

    public IVideoControls? Controls { get; private set; }

    public double VideoAspect => _aspect;

    public double FrameX => CurrentPosition.X;

    public double FrameY => CurrentPosition.Y;

    public void Attach(ChroaMpvContext mpv, IVideoOverlayHost? overlay, IVideoControls? controls)
    {
        Detach();

        _mpv = mpv;
        Overlay = overlay;
        Controls = controls;

        _mpv.SubtitlesChanged += OnSubtitlesChanged;
        _mpv.MediaOpened += OnMediaOpened;
        _mpv.VideoAspectDetected += OnVideoAspectDetected;

        CanAddSubtitle = !string.IsNullOrEmpty(mpv.CurrentFile);

        ShowTracks(mpv.Subtitles);
    }

    public void Detach()
    {
        if (_mpv is not null)
        {
            _mpv.SubtitlesChanged -= OnSubtitlesChanged;
            _mpv.MediaOpened -= OnMediaOpened;
            _mpv.VideoAspectDetected -= OnVideoAspectDetected;
        }

        _mpv = null;

        IsFrameVisible = false;

        Controls?.EnableControls();
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

    public void ToggleFrame() => IsFrameVisible = !IsFrameVisible;

    public void DisableControls() => Controls?.DisableControls();

    public void EnableControls() => Controls?.EnableControls();

    public void MoveFrame(double xPercent, double yPercent)
    {
        var id = PlayingTrackId;

        if (id <= 0) return;

        _positions[id] = (Math.Clamp(xPercent, 0, 100), Math.Clamp(yPercent, 0, 100));

        _mpv?.SetSubtitleVerticalPosition(yPercent);

        OnPropertyChanged(nameof(FrameX));
        OnPropertyChanged(nameof(FrameY));
    }

    private int PlayingTrackId => Tracks.FirstOrDefault(track => track.IsSelected)?.Id ?? 0;

    private (double X, double Y) CurrentPosition =>
        _positions.TryGetValue(PlayingTrackId, out var value) ? value : (50, 88);

    private void OnMediaOpened(object? sender, EventArgs e) =>
        Dispatcher.UIThread.Post(() => CanAddSubtitle = true);

    private void OnVideoAspectDetected(object? sender, double aspect) =>
        Dispatcher.UIThread.Post(() =>
        {
            _aspect = double.IsFinite(aspect) && aspect > 0 ? aspect : 16.0 / 9.0;

            OnPropertyChanged(nameof(VideoAspect));
        });

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
