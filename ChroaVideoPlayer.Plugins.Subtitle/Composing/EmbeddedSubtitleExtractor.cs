using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using ChroaVideoPlayer.Core.Utils;

namespace ChroaVideoPlayer.Plugins.Subtitle;

public sealed record ExtractedSubtitle(int StreamOrder, string Path);

public static class EmbeddedSubtitleExtractor
{
    private const int MaxStreams = 16;

    private static readonly TaskRunner Runner = new(TimeSpan.FromSeconds(60));

    public static async Task<IReadOnlyList<ExtractedSubtitle>> ExtractAsync(string videoPath, string cacheRoot)
    {
        var result = new List<ExtractedSubtitle>();

        if (string.IsNullOrEmpty(videoPath) || !File.Exists(videoPath)) return result;

        if (FfmpegLocator.Resolve() is not { } ffmpeg) return result;

        var stamp = File.GetLastWriteTimeUtc(videoPath).Ticks;
        var key = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes($"{videoPath}|{stamp}")))[..16];

        Directory.CreateDirectory(cacheRoot);

        for (var order = 0; order < MaxStreams; order++)
        {
            var target = Path.Combine(cacheRoot, $"{key}-s{order}.srt");

            if (!File.Exists(target) && !await TryExtractAsync(ffmpeg, videoPath, order, target)) break;

            if (File.Exists(target) && new FileInfo(target).Length > 0)
                result.Add(new ExtractedSubtitle(order, target));
        }

        return result;
    }

    private static async Task<bool> TryExtractAsync(string ffmpeg, string videoPath, int order, string target)
    {
        var result = await Runner.RunAsync(
            ffmpeg,
            new[]
            {
                "-y",
                "-v",
                "error",
                "-i",
                videoPath,
                "-map",
                $"0:s:{order}",
                "-f",
                "srt",
                target,
            });

        return result is { Succeeded: true } && File.Exists(target) && new FileInfo(target).Length > 0;
    }
}
