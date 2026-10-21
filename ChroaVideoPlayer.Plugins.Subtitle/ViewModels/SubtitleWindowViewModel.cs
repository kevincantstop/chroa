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
    private readonly HashSet<int> _included = new();

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
    [NotifyPropertyChangedFor(nameof(CanCompose))]
    public partial bool CanAddSubtitle { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCompose))]
    public partial bool IsComposing { get; set; }

    [ObservableProperty]
    public partial string ComposeStatus { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsFrameVisible { get; set; }

    public bool CanCompose => CanAddSubtitle && !IsComposing;

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

        _compositions.Clear();
        _active = null;

        _included.Clear();

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

        if (IncludedCount >= 2) return "Select a single subtitle to change its position.";

        if (GetComposition(row) is { } existing)
        {
            if (existing.Sources.Count > 1) return "A composed subtitle is final and cannot be repositioned.";

            _active = existing;

            IsFrameVisible = true;

            return null;
        }

        if (await ComposeRowsAsync(new[] { row }) is { } error) return error;

        IsFrameVisible = true;

        return null;
    }

    public async Task<string?> ComposeMergedAsync()
    {
        if (IncludedCount < 2) return "Select at least two subtitles to compose.";

        return await ComposeRowsAsync(Tracks.Where(track => _included.Contains(track.Id)).ToList());
    }

    private async Task<string?> ComposeRowsAsync(IReadOnlyList<SubtitleTrackRow> rows)
    {
        if (_mpv is not { } mpv) return "The player is not ready.";

        IsComposing = true;

        try
        {
            if (rows.Count == 0) return "Select subtitle tracks to compose first.";

            var built = new List<ComposedSource>();
            var unresolved = new List<string>();

            foreach (var row in rows)
            {
                if (SourceKeyForRow(row) is not { } key)
                {
                    unresolved.Add(row.Label);

                    continue;
                }

                if (row.IsExternal)
                {
                    var loaded = SubtitleSourceLoader.Load(row.ExternalFileName);

                    if (loaded.Count == 0)
                    {
                        unresolved.Add(row.Label);

                        continue;
                    }

                    foreach (var source in loaded)
                    {
                        built.Add(new ComposedSource(
                            $"{source.Key}|{built.Count}",
                            source.Label,
                            source.Cues)
                        {
                            XPercent = source.XPercent,
                            YPercent = source.YPercent,
                            HasPosition = source.HasPosition,
                        });
                    }

                    continue;
                }

                ComposeStatus = "Reading subtitle streams...";

                if (mpv.CurrentFile is not { } video || !File.Exists(video))
                {
                    unresolved.Add(row.Label);

                    continue;
                }

                if (!_streamMaps.TryGetValue(video, out var map))
                {
                    map = await SubtitleStreamMap.ReadAsync(video);

                    _streamMaps[video] = map;
                }

                var embeddedTracks = Tracks.Where(track => !track.IsExternal).Select(track => track.Info).ToList();
                var streamIndex = SubtitleStreamMap.Resolve(map, embeddedTracks, row.Info);

                if (streamIndex < 0)
                {
                    unresolved.Add(row.Label);

                    continue;
                }

                ComposeStatus = "Extracting embedded subtitles...";

                if (await EmbeddedSubtitleExtractor.ExtractAsync(video, SubtitleCompositionPath.Root, streamIndex) is not { } extracted)
                {
                    unresolved.Add(row.Label);

                    continue;
                }

                var extractedCues = SrtParser.Load(extracted.Path);

                if (extractedCues.Count > 0) built.Add(new ComposedSource(key, row.Label, extractedCues));
                else unresolved.Add(row.Label);
            }

            if (built.Count == 0)
            {
                return unresolved.Count > 0
                    ? $"Could not read: {string.Join(", ", unresolved)}"
                    : "No subtitle source found.";
            }

            for (var index = 0; index < built.Count; index++)
            {
                if (built[index].HasPosition) continue;

                built[index].YPercent = AssComposer.SlotPercent(index, built.Count);
            }

            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var title = built.Count == 1
                ? $"{Sanitize(built[0].Label)}_{stamp}"
                : $"Composed_{stamp}";

            var composition = new SubtitleComposition(
                title,
                Path.Combine(SubtitleCompositionPath.Root, $"{title}.ass"),
                built,
                built[0]);

            _compositions[title] = composition;
            _active = composition;

            ComposeStatus = "Writing subtitles...";

            return await ApplyCompositionAsync(mpv);
        }
        finally
        {
            ComposeStatus = string.Empty;

            IsComposing = false;
        }
    }

    private SubtitleComposition? GetComposition(SubtitleTrackRow row) =>
        _compositions.TryGetValue(CompositionName(row.Label), out var composition) ? composition : null;

    private static string CompositionName(string label) =>
        label.EndsWith(".ass", StringComparison.OrdinalIgnoreCase) ? label[..^4] : label;

    private string? SourceKeyForRow(SubtitleTrackRow row)
    {
        if (!row.IsExternal) return $"emb:{row.Id}";

        return string.IsNullOrEmpty(row.ExternalFileName) ? null : $"file:{row.ExternalFileName}";
    }

    public void ToggleInclude(SubtitleTrackRow row)
    {
        if (!_included.Add(row.Id)) _included.Remove(row.Id);

        ApplyIncluded();
    }

    private void ApplyIncluded()
    {
        foreach (var track in Tracks)
        {
            track.IsIncluded = _included.Contains(track.Id);
            track.CanPosition = CanPosition(track);
        }

        OnPropertyChanged(nameof(CompositionSummary));
        OnPropertyChanged(nameof(IncludedCount));
        OnPropertyChanged(nameof(CanMerge));
    }

    private int IncludedCount => _included.Count;

    public bool CanMerge => IncludedCount >= 2 && !IsComposing;

    public string CompositionSummary => _included.Count switch
    {
        0 => string.Empty,
        1 => "1 subtitle selected",
        _ => $"{_included.Count} subtitles selected",
    };

    private bool CanPosition(SubtitleTrackRow row)
    {
        if (IsComposing) return false;
        if (_included.Count >= 2) return false;

        return GetComposition(row) is not { Sources.Count: > 1 };
    }

    partial void OnIsComposingChanged(bool value) => ApplyIncluded();

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

    private static string Sanitize(string name)
    {
        var builder = new StringBuilder(name.Length);

        foreach (var character in name)
            builder.Append(Path.GetInvalidFileNameChars().Contains(character) ? '_' : character);

        var result = builder.ToString().Trim();

        return string.IsNullOrEmpty(result) ? "composed" : result;
    }

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

        ApplyIncluded();
    }

    private void ApplyCurrent()
    {
        foreach (var track in Tracks) track.IsCurrent = track.Id == _currentId;
    }
}
