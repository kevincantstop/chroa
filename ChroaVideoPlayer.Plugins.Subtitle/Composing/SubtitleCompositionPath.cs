using System;
using System.IO;

namespace ChroaVideoPlayer.Plugins.Subtitle;

public static class SubtitleCompositionPath
{
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ChroaVideoPlayer",
        "subtitles");
}
