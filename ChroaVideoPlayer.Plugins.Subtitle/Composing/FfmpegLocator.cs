using System;
using System.IO;

namespace ChroaVideoPlayer.Plugins.Subtitle;

public static class FfmpegLocator
{
    public static string? Resolve()
    {
        var name = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";

        foreach (var directory in Candidates())
        {
            var candidate = Path.Combine(directory, name);

            if (File.Exists(candidate)) return candidate;
        }

        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;

        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(directory.Trim(), name);

            if (File.Exists(candidate)) return candidate;
        }

        return null;
    }

    private static string[] Candidates() =>
    [
        Path.Combine(AppContext.BaseDirectory, "native"),
        Path.Combine(AppContext.BaseDirectory, "ffmpeg"),
    ];
}
