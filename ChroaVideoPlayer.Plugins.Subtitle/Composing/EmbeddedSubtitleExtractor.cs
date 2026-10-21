using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using ChroaVideoPlayer.Core.Utils;

namespace ChroaVideoPlayer.Plugins.Subtitle;

public sealed record ExtractedSubtitle(int StreamOrder, string Path);

public static class EmbeddedSubtitleExtractor
{
    private static readonly TaskRunner Runner = new(TimeSpan.FromMinutes(5));

    public static async Task<ExtractedSubtitle?> ExtractAsync(
        string videoPath,
        string cacheRoot,
        int subtitleOrder)
    {
        if (string.IsNullOrEmpty(videoPath) || !File.Exists(videoPath)) return null;
        if (subtitleOrder < 0) return null;
        if (FfmpegLocator.Resolve() is not { } ffmpeg) return null;

        var stamp = File.GetLastWriteTimeUtc(videoPath).Ticks;
        var key = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes($"{videoPath}|{stamp}")))[..16];

        Directory.CreateDirectory(cacheRoot);

        var target = Path.Combine(cacheRoot, $"{key}-s{subtitleOrder}.srt");

        if (File.Exists(target) && new FileInfo(target).Length == 0) File.Delete(target);

        if (!File.Exists(target) && !await TryExtractAsync(ffmpeg, videoPath, subtitleOrder, target)) return null;

        return new ExtractedSubtitle(subtitleOrder, target);
    }

    private static async Task<bool> TryExtractAsync(string ffmpeg, string videoPath, int order, string target)
    {
        var result = await Runner.RunAsync(
            ffmpeg,
            new[]
            {
                "-y",
                "-nostdin",
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

        if (result is { Succeeded: true } && File.Exists(target) && new FileInfo(target).Length > 0) return true;

        if (File.Exists(target)) File.Delete(target);

        return false;
    }
}
