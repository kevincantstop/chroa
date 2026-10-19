using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using HanumanInstitute.LibMpv;

namespace ChroaVideoPlayer.Core.Mpv;

public readonly record struct SubtitleTrackInfo(
    int Id,
    string Label,
    bool IsSelected,
    string Language,
    bool IsExternal,
    string Codec);

public readonly record struct SubtitleState(IReadOnlyList<SubtitleTrackInfo> Tracks, bool IsVisible);

public sealed partial class ChroaMpvContext
{
    private static readonly SubtitleState NoSubtitles = new(Array.Empty<SubtitleTrackInfo>(), false);

    private SubtitleState _subtitles = NoSubtitles;

    public event EventHandler<SubtitleState>? SubtitlesChanged;

    public SubtitleState Subtitles => _subtitles;

    public void SelectSubtitle(int id)
    {
        var context = _context;
        if (context is null) return;

        SafeRun(nameof(SelectSubtitle), () =>
        {
            if (id <= 0)
            {
                context.SubId.SetNo();
            }
            else
            {
                context.SubId.Set(id);
                context.SubVisibility.Set(true);
            }

            _ = RefreshSubtitlesLaterAsync();
        });
    }

    public void LoadSubtitleFile(string path)
    {
        var context = _context;
        if (context is null || string.IsNullOrWhiteSpace(path)) return;

        _ = AddSubtitleAsync(context, path);
    }

    private async Task AddSubtitleAsync(MpvContext context, string path)
    {
        try
        {
            await context.SubAdd(path).InvokeAsync();
            await RefreshSubtitlesLaterAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"sub-add failed: {ex}");
        }
    }

    private async Task RefreshSubtitlesAsync()
    {
        for (var i = 0; i < 10; i++)
        {
            if (TryReadSubtitles(out var state))
            {
                PublishSubtitles(state);
                return;
            }

            await Task.Delay(100);
        }
    }

    private async Task RefreshSubtitlesLaterAsync()
    {
        await Task.Delay(200);
        await RefreshSubtitlesAsync();
    }

    private bool TryReadSubtitles(out SubtitleState state)
    {
        state = NoSubtitles;

        var context = _context;
        if (context is null) return false;

        try
        {
            var count = context.TrackListCount.Get() ?? 0;
            var tracks = new List<SubtitleTrackInfo>();
            var selected = 0;

            for (var i = 0; i < count; i++)
            {
                if (!string.Equals(context.TrackListType[i].Get(), "sub", StringComparison.Ordinal)) continue;

                var id = context.TrackListId[i].Get() ?? 0;
                if (id <= 0) continue;

                var isSelected = context.TrackListIsSelected[i].Get() ?? false;
                if (isSelected) selected = id;

                var language = context.TrackListLanguage[i].Get() ?? string.Empty;
                var isExternal = context.TrackListIsExternal[i].Get() ?? false;
                var codec = context.TrackListCodec[i].Get() ?? string.Empty;

                tracks.Add(new SubtitleTrackInfo(
                    id,
                    DescribeSubtitleTrack(context, i, id),
                    isSelected,
                    language,
                    isExternal,
                    codec));
            }

            if (context.SubId.GetNo()) selected = 0;

            state = new SubtitleState(tracks, selected > 0 && (context.SubVisibility.Get() ?? true));
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"subtitle tracks not ready yet ({ex.GetType().Name})");
            return false;
        }
    }

    private static string DescribeSubtitleTrack(MpvContext context, int index, int id)
    {
        var language = context.TrackListLanguage[index].Get();

        if (!string.IsNullOrWhiteSpace(language) && !language.Equals("und", StringComparison.OrdinalIgnoreCase))
        {
            return LanguageLabel(language);
        }

        return $"Track {id}";
    }

    private static string LanguageLabel(string code) => code.ToLowerInvariant() switch
    {
        "eng" or "en" => "English",
        "chi" or "zho" or "cht" or "chs" or "zh" => "Chinese",
        "jpn" or "ja" => "Japanese",
        "kor" or "ko" => "Korean",
        "fra" or "fre" or "fr" => "French",
        "deu" or "ger" or "de" => "German",
        "spa" or "es" => "Spanish",
        "rus" or "ru" => "Russian",
        "por" or "pt" => "Portuguese",
        "ita" or "it" => "Italian",
        "ara" or "ar" => "Arabic",
        "hin" or "hi" => "Hindi",
        "tha" or "th" => "Thai",
        "vie" or "vi" => "Vietnamese",
        _ => code.ToUpperInvariant(),
    };

    private void PublishSubtitles(SubtitleState state)
    {
        _subtitles = state;

        SubtitlesChanged?.Invoke(this, state);
    }
}
