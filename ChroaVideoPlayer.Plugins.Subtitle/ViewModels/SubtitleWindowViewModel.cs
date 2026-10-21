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
    private readonly List<ComposedSource> _sources = new();

    private readonly Dictionary<string, (double X, double Y)> _sourcePositions = new();

    private readonly List<string> _importedPaths = new();

    private ChroaMpvContext? _mpv;

    private ComposedSource? _editedSource;

    private CancellationTokenSource? _applyDebounce;

    private string? _compositionPath;

    private bool _compositionMounted;

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
        _editedSource is { } source ? (source.XPercent, source.YPercent) : (50, 88);

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

        _compositionPath = null;
        _compositionMounted = false;
        _editedSource = null;

        _sources.Clear();

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
        if (_importedPaths.Count > 0)
        {
            return _importedPaths.Count == 1
                ? Path.GetFileNameWithoutExtension(_importedPaths[0])
                : $"Subtitles ({_importedPaths.Count})";
        }

        return "Embedded subtitles";
    }

    public async Task<string?> ComposeAsync()
    {
        if (_mpv is not { } mpv) return "The player is not ready.";

        IsComposing = true;

        try
        {
            var built = new List<ComposedSource>();

            foreach (var path in _importedPaths)
            {
                var cues = SrtParser.Load(path);

                if (cues.Count > 0) built.Add(new ComposedSource($"file:{path}", Path.GetFileNameWithoutExtension(path), cues));
            }

            var order = PlayingEmbeddedOrder();

            if (order >= 0 && mpv.CurrentFile is { } video && File.Exists(video))
            {
                ComposeStatus = "Extracting embedded subtitles...";

                if (await EmbeddedSubtitleExtractor.ExtractAsync(video, SubtitleCompositionPath.Root, order) is { } extracted)
                {
                    var cues = SrtParser.Load(extracted.Path);

                    if (cues.Count > 0) built.Add(new ComposedSource($"emb:{order}", "Embedded", cues));
                }
            }

            if (built.Count == 0)
                return "No subtitle source found - add a subtitle file, or select an embedded subtitle.";

            ComposedSource? edited = null;

            for (var index = 0; index < built.Count; index++)
            {
                var source = built[index];

                if (_sourcePositions.TryGetValue(source.Key, out var saved))
                {
                    source.XPercent = saved.X;
                    source.YPercent = saved.Y;
                }
                else
                {
                    source.YPercent = AssComposer.SlotPercent(index, built.Count);
                }

                if (source.Key == $"emb:{order}") edited = source;
            }

            _sources.Clear();
            _sources.AddRange(built);

            _editedSource = edited ?? _sources[^1];

            var title = SuggestComposeName();

            _compositionPath = Path.Combine(SubtitleCompositionPath.Root, $"{Sanitize(title)}.ass");
            _compositionMounted = false;

            ComposeStatus = "Writing subtitles...";

            return await ApplyCompositionAsync(mpv);
        }
        finally
        {
            ComposeStatus = string.Empty;

            IsComposing = false;
        }
    }

    public async Task<string?> ShowMovementAsync()
    {
        if (_compositionPath is null || _sources.Count == 0)
        {
            var error = await ComposeAsync();

            if (error is not null) return error;
        }

        IsFrameVisible = true;

        return null;
    }

    public void MoveEditedSource(double xPercent, double yPercent)
    {
        if (_editedSource is not { } source) return;

        source.XPercent = Math.Clamp(xPercent, 0, 100);
        source.YPercent = Math.Clamp(yPercent, 0, 100);

        _sourcePositions[source.Key] = (source.XPercent, source.YPercent);

        ScheduleApply();
    }

    public void DisableControls() => Controls?.DisableControls();

    public void EnableControls() => Controls?.EnableControls();

    private async Task<string?> ApplyCompositionAsync(ChroaMpvContext mpv)
    {
        if (_compositionPath is not { } path) return "Nothing to compose.";

        var title = Path.GetFileNameWithoutExtension(path);
        var (width, height) = mpv.GetVideoSize();
        var content = AssComposer.Build(_sources, width, height, title);

        Directory.CreateDirectory(SubtitleCompositionPath.Root);

        await File.WriteAllTextAsync(path, content, new UTF8Encoding(false));

        if (_compositionMounted && mpv.ReloadSubtitle()) return null;

        mpv.LoadSubtitleFile(path);

        _compositionMounted = true;

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

    private int PlayingEmbeddedOrder()
    {
        var order = -1;

        foreach (var track in Tracks)
        {
            if (track.IsExternal) continue;

            order++;

            if (track.IsSelected) return order;
        }

        return -1;
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
