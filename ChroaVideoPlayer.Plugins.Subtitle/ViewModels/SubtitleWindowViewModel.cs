using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using ChroaVideoPlayer.Core.Mpv;
using ChroaVideoPlayer.Core.Plugin.Context;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ChroaVideoPlayer.Plugins.Subtitle;

public partial class SubtitleWindowViewModel : ObservableObject
{
    private readonly Dictionary<string, SubtitleComposition> _compositions = new();

    private readonly Dictionary<string, IReadOnlyList<SubtitleStreamInfo>> _streamMaps = new();

    private ChroaMpvContext? _mpv;

    private SubtitleComposition? _active;

    private CancellationTokenSource? _applyDebounce;

    private int _currentId;

    private double _aspect = 16.0 / 9.0;

    public SubtitleWindowViewModel()
    {
        Tracks.CollectionChanged += (_, _) => OnPropertyChanged(nameof(IsEmpty));
    }

    public ObservableCollection<SubtitleTrackRow> Tracks { get; } = new();

    public bool IsEmpty => Tracks.Count == 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAddSubtitle))]
    public partial bool IsComposing { get; set; }

    [ObservableProperty]
    public partial string ComposeStatus { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsFrameVisible { get; set; }

    public bool CanAddSubtitle => !IsComposing;

    public IVideoOverlayHost? Overlay { get; private set; }

    public IVideoControls? Controls { get; private set; }

    public double VideoAspect => _aspect;

    public (double X, double Y) EditedSourcePosition =>
        _active?.EditedSource is { } source ? (source.XPercent, source.YPercent) : (50, 88);

    public void Attach(ChroaMpvContext mpv, IVideoOverlayHost? overlay, IVideoControls? controls)
    {
        Detach();

        _mpv = mpv;
        Overlay = overlay;
        Controls = controls;

        _mpv.SubtitlesChanged += OnSubtitlesChanged;
        _mpv.VideoAspectDetected += OnVideoAspectDetected;

        ShowTracks(mpv.Subtitles);
    }

    public void Detach()
    {
        if (_mpv is not null)
        {
            _mpv.SubtitlesChanged -= OnSubtitlesChanged;
            _mpv.VideoAspectDetected -= OnVideoAspectDetected;
        }

        _mpv = null;

        _compositions.Clear();
        _active = null;

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

    public async Task<string?> ShowMovementAsync(SubtitleTrackRow? row)
    {
        if (row is null) return "Select a subtitle first.";

        if (GetComposition(row) is { } existing)
        {
            _active = existing;

            IsFrameVisible = true;

            return null;
        }

        if (await ComposeSingleAsync(row) is { } error) return error;

        IsFrameVisible = true;

        return null;
    }

    public void MoveEditedSource(double xPercent, double yPercent)
    {
        if (_active is not { } composition) return;

        var source = composition.EditedSource;

        source.XPercent = Math.Clamp(xPercent, 0, 100);
        source.YPercent = Math.Clamp(yPercent, 0, 100);

        ScheduleApply();
    }

    public void DisableControls() => Controls?.DisableControls();

    public void EnableControls() => Controls?.EnableControls();

    private async Task<string?> ComposeSingleAsync(SubtitleTrackRow row)
    {
        if (_mpv is not { } mpv) return "The player is not ready.";

        IsComposing = true;

        try
        {
            ComposeStatus = "Reading subtitle...";

            var sources = await BuildSourcesAsync(row);

            if (sources.Count == 0) return $"Could not read: {row.Label}";

            var title = $"{Sanitize(row.Label)}_{DateTime.Now:yyyyMMdd-HHmmss}";
            var composition = new SubtitleComposition(
                title,
                Path.Combine(SubtitleCompositionPath.Root, $"{title}.ass"),
                sources,
                sources[0]);

            _compositions[title] = composition;
            _active = composition;

            ComposeStatus = "Writing subtitle...";

            return await ApplyCompositionAsync(mpv);
        }
        finally
        {
            ComposeStatus = string.Empty;

            IsComposing = false;
        }
    }

    private async Task<List<ComposedSource>> BuildSourcesAsync(SubtitleTrackRow row) =>
        row.IsExternal
            ? SubtitleSourceLoader.Load(row.ExternalFileName).ToList()
            : await BuildEmbeddedSourcesAsync(row);

    private async Task<List<ComposedSource>> BuildEmbeddedSourcesAsync(SubtitleTrackRow row)
    {
        var sources = new List<ComposedSource>();

        if (_mpv is not { } mpv) return sources;
        if (mpv.CurrentFile is not { } video || !File.Exists(video)) return sources;

        ComposeStatus = "Reading subtitle streams...";

        if (!_streamMaps.TryGetValue(video, out var map))
        {
            map = await SubtitleStreamMap.ReadAsync(video);

            _streamMaps[video] = map;
        }

        var embeddedTracks = Tracks.Where(track => !track.IsExternal).Select(track => track.Info).ToList();
        var streamIndex = SubtitleStreamMap.Resolve(map, embeddedTracks, row.Info);

        if (streamIndex < 0) return sources;

        ComposeStatus = "Extracting subtitle...";

        if (await EmbeddedSubtitleExtractor.ExtractAsync(video, SubtitleCompositionPath.Root, streamIndex) is not { } extracted)
            return sources;

        var cues = SrtParser.Load(extracted.Path);

        if (cues.Count > 0) sources.Add(new ComposedSource($"emb:{row.Id}", row.Label, cues));

        return sources;
    }

    private async Task<string?> ApplyCompositionAsync(ChroaMpvContext mpv)
    {
        if (_active is not { } composition) return "Nothing to compose.";

        var (width, height) = mpv.GetVideoSize();
        var content = AssComposer.Build(composition.Sources, width, height, composition.Title);

        Directory.CreateDirectory(SubtitleCompositionPath.Root);

        await File.WriteAllTextAsync(composition.Path, content, new UTF8Encoding(false));

        if (composition.IsMounted && mpv.ReloadSubtitle()) return null;

        mpv.LoadSubtitleFile(composition.Path);

        composition.IsMounted = true;

        return null;
    }

    private void ScheduleApply()
    {
        _applyDebounce?.Cancel();
        _applyDebounce?.Dispose();

        var debounce = new CancellationTokenSource();

        _applyDebounce = debounce;

        _ = ApplyLaterAsync(debounce.Token);
    }

    private async Task ApplyLaterAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(150, token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (token.IsCancellationRequested || _mpv is not { } mpv) return;

        await ApplyCompositionAsync(mpv);
    }

    private SubtitleComposition? GetComposition(SubtitleTrackRow row) =>
        _compositions.TryGetValue(CompositionName(row.Label), out var composition) ? composition : null;

    private static string CompositionName(string label) =>
        label.EndsWith(".ass", StringComparison.OrdinalIgnoreCase) ? label[..^4] : label;

    private static string Sanitize(string name)
    {
        var builder = new StringBuilder(name.Length);

        foreach (var character in name)
            builder.Append(Path.GetInvalidFileNameChars().Contains(character) ? '_' : character);

        var result = builder.ToString().Trim();

        return string.IsNullOrEmpty(result) ? "subtitle" : result;
    }

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
