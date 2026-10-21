using System;
using System.IO;

namespace ChroaVideoPlayer.Plugins.Subtitle;

public static class ToolLocator
{
    public static string? Resolve(string name)
    {
        var fileName = OperatingSystem.IsWindows() ? $"{name}.exe" : name;

        foreach (var directory in new[]
                 {
                     Path.Combine(AppContext.BaseDirectory, "native"),
                     Path.Combine(AppContext.BaseDirectory, "ffmpeg"),
                 })
        {
            var candidate = Path.Combine(directory, fileName);

            if (File.Exists(candidate)) return candidate;
        }

        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;

        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(directory.Trim(), fileName);

            if (File.Exists(candidate)) return candidate;
        }

        return null;
    }
}
