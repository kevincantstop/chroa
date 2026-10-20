using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Threading;
using ChroaVideoPlayer.Core.Mpv;
using ChroaVideoPlayer.Core.Plugin.Context;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ChroaVideoPlayer.Plugins.Subtitle;

public partial class SubtitleWindowViewModel : ObservableObject
{
    private readonly Dictionary<int, (double X, double Y)> _positions = new();

    private readonly List<string> _importedPaths = new();

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
        _mpv.SubtitleFileLoaded += OnSubtitleFileLoaded;

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
            _mpv.SubtitleFileLoaded -= OnSubtitleFileLoaded;
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

    public string SuggestComposeName()
    {
        if (_importedPaths.Count == 0) return "Composed subtitles";

        return _importedPaths.Count == 1
            ? Path.GetFileNameWithoutExtension(_importedPaths[0])
            : $"Subtitles ({_importedPaths.Count})";
    }

    public async Task<string?> ComposeAsync(string name)
    {
        if (_mpv is not { } mpv) return "The player is not ready.";

        var sources = new List<(string Label, string Path)>();

        foreach (var path in _importedPaths) sources.Add((Path.GetFileNameWithoutExtension(path), path));

        if (mpv.CurrentFile is { } video && File.Exists(video))
        {
            var extracted = await EmbeddedSubtitleExtractor.ExtractAsync(video, SubtitleCompositionPath.Root);

            foreach (var item in extracted) sources.Add(($"Embedded {item.StreamOrder + 1}", item.Path));
        }

        if (sources.Count == 0)
            return "No subtitle source found - add a subtitle file, or install ffmpeg to read embedded subtitles.";

        var tracks = new List<ComposedTrack>();

        foreach (var source in sources)
        {
            var cues = SrtParser.Load(source.Path);

            if (cues.Count > 0) tracks.Add(new ComposedTrack(source.Label, cues));
        }

        if (tracks.Count == 0) return "No subtitle cues could be read from the available sources.";

        var title = string.IsNullOrWhiteSpace(name) ? "Composed subtitles" : name.Trim();
        var (width, height) = mpv.GetVideoSize();
        var content = AssComposer.Build(tracks, width, height, title);

        Directory.CreateDirectory(SubtitleCompositionPath.Root);

        var target = Path.Combine(SubtitleCompositionPath.Root, $"{Sanitize(title)}.ass");

        File.WriteAllText(target, content, new UTF8Encoding(false));

        mpv.LoadSubtitleFile(target);

        return null;
    }

    private static string Sanitize(string name)
    {
        var builder = new StringBuilder(name.Length);

        foreach (var character in name)
            builder.Append(Path.GetInvalidFileNameChars().Contains(character) ? '_' : character);

        var result = builder.ToString().Trim();

        return string.IsNullOrEmpty(result) ? "composed" : result;
    }

    public void ToggleFrame() => IsFrameVisible = !IsFrameVisible;

    public void DisableControls() => Controls?.DisableControls();

    public void EnableControls() => Controls?.EnableControls();

    public void MoveFrame(double xPercent, double yPercent)
    {
        var id = PlayingTrackId;

        if (id <= 0) return;

        _positions[id] = (Math.Clamp(xPercent, 0, 100), Math.Clamp(yPercent, 0, 100));

        OnPropertyChanged(nameof(FrameX));
        OnPropertyChanged(nameof(FrameY));
    }

    public void ApplySubtitlePosition(double subtitlePosition) =>
        _mpv?.SetSubtitleVerticalPosition(subtitlePosition);

    private int PlayingTrackId => Tracks.FirstOrDefault(track => track.IsSelected)?.Id ?? 0;

    private (double X, double Y) CurrentPosition =>
        _positions.TryGetValue(PlayingTrackId, out var value) ? value : (50, 88);

    private void OnMediaOpened(object? sender, EventArgs e) =>
        Dispatcher.UIThread.Post(() => CanAddSubtitle = true);

    private void OnSubtitleFileLoaded(object? sender, string path)
    {
        if (string.IsNullOrEmpty(path)) return;

        if (Dispatcher.UIThread.CheckAccess())
        {
            TrackImport(path);

            return;
        }

        Dispatcher.UIThread.Post(() => TrackImport(path));
    }

    private void TrackImport(string path)
    {
        if (path.StartsWith(SubtitleCompositionPath.Root, StringComparison.OrdinalIgnoreCase)) return;

        if (!_importedPaths.Contains(path)) _importedPaths.Add(path);
    }

    private void OnVideoAspectDetected(object? sender, double aspect) =>
        Dispatcher.UIThread.Post(() =>
        {
            _aspect = double.IsFinite(aspect) && aspect > 0 ? aspect : 16.0 / 9.0;

            OnPropertyChanged(nameof(VideoAspect));
        });

    private void OnSubtitlesChanged(object? sender, SubtitleState state)    {
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
