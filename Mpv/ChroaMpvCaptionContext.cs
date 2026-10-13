using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using HanumanInstitute.LibMpv;

namespace ChroaVideoPlayer.Mpv;

public readonly record struct SubtitleTrackInfo(int Id, string Label, bool IsSelected);

public readonly record struct SubtitleState(IReadOnlyList<SubtitleTrackInfo> Tracks, bool IsVisible);

public sealed partial class ChroaMpvContext
{
    private static readonly SubtitleState NoSubtitles = new(Array.Empty<SubtitleTrackInfo>(), false);

    public event EventHandler<SubtitleState>? SubtitlesChanged;

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

                tracks.Add(new SubtitleTrackInfo(id, DescribeSubtitleTrack(context, i, id), isSelected));
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
        var codec = context.TrackListCodec[index].Get();
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(language) && !language.Equals("und", StringComparison.OrdinalIgnoreCase))
        {
            parts.Add(LanguageLabel(language));
        }

        if (context.TrackListIsExternal[index].Get() ?? false) parts.Add("External");
        if (context.TrackListIsForced[index].Get() ?? false) parts.Add("Forced");
        if (!string.IsNullOrWhiteSpace(codec)) parts.Add(codec);

        return parts.Count > 0 ? string.Join(" · ", parts) : $"Track {id}";
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

    private void PublishSubtitles(SubtitleState state) => SubtitlesChanged?.Invoke(this, state);
}
