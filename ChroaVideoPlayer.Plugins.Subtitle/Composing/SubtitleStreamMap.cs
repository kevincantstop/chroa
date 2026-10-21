using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using ChroaVideoPlayer.Core.Mpv;
using ChroaVideoPlayer.Core.Utils;

namespace ChroaVideoPlayer.Plugins.Subtitle;

public sealed record SubtitleStreamInfo(int Index, string Language, string Title);

public static class SubtitleStreamMap
{
    private static readonly TaskRunner Runner = new(TimeSpan.FromSeconds(30));

    public static async Task<IReadOnlyList<SubtitleStreamInfo>> ReadAsync(string videoPath)
    {
        var result = new List<SubtitleStreamInfo>();

        if (string.IsNullOrEmpty(videoPath) || !File.Exists(videoPath)) return result;

        if (ToolLocator.Resolve("ffprobe") is not { } ffprobe) return result;

        var probe = await Runner.RunAsync(
            ffprobe,
            new[]
            {
                "-v",
                "error",
                "-select_streams",
                "s",
                "-show_entries",
                "stream=index:stream_tags=language,title",
                "-of",
                "json",
                videoPath,
            });

        if (probe is not { Succeeded: true } probed || string.IsNullOrWhiteSpace(probed.Output)) return result;

        try
        {
            using var document = JsonDocument.Parse(probed.Output);

            if (!document.RootElement.TryGetProperty("streams", out var streams)) return result;

            foreach (var stream in streams.EnumerateArray())
            {
                if (!stream.TryGetProperty("index", out var index)) continue;

                var language = string.Empty;
                var title = string.Empty;

                if (stream.TryGetProperty("tags", out var tags))
                {
                    if (tags.TryGetProperty("language", out var languageValue)) language = languageValue.GetString() ?? string.Empty;
                    if (tags.TryGetProperty("title", out var titleValue)) title = titleValue.GetString() ?? string.Empty;
                }

                result.Add(new SubtitleStreamInfo(index.GetInt32(), language, title));
            }
        }
        catch (JsonException ex)
        {
            Debug.WriteLine($"ffprobe output not parsable ({ex.Message})");
        }

        return result;
    }

    public static int Resolve(
        IReadOnlyList<SubtitleStreamInfo> streams,
        IReadOnlyList<SubtitleTrackInfo> tracks,
        SubtitleTrackInfo track)
    {
        var sameLanguage = streams
            .Where(stream => Matches(stream.Language, track.Language))
            .ToList();

        if (sameLanguage.Count == 0) return -1;

        if (!string.IsNullOrWhiteSpace(track.Title))
        {
            var byTitle = sameLanguage
                .Where(stream => stream.Title.Equals(track.Title, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (byTitle.Count > 0) return byTitle[0].Index;
        }

        var sameLanguageTracks = tracks
            .Where(item => Matches(item.Language, track.Language))
            .ToList();

        var rank = sameLanguageTracks.IndexOf(track);

        if (rank >= 0 && rank < sameLanguage.Count) return sameLanguage[rank].Index;

        return sameLanguage[0].Index;
    }

    private static bool Matches(string left, string right) =>
        !string.IsNullOrWhiteSpace(left) &&
        !string.IsNullOrWhiteSpace(right) &&
        left.Equals(right, StringComparison.OrdinalIgnoreCase);
}
