using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Platform.Storage;

namespace ChroaVideoPlayer.Core.Utils;

public static class SubtitleFileTypes
{
    public static IReadOnlyList<string> Extensions { get; } = new[]
    {
        ".srt", ".ass", ".ssa", ".sub", ".vtt", ".sup", ".idx", ".lrc",
    };

    public static FilePickerFileType Subtitle { get; } = new("Subtitle Files")
    {
        Patterns = Extensions.Select(extension => "*" + extension).ToArray(),
        MimeTypes = new[] { "text/*" },
        AppleUniformTypeIdentifiers = new[] { "public.text" },
    };

    public static IReadOnlyList<FilePickerFileType> Filter { get; } = new[] { Subtitle, VideoFileTypes.All };

    public static bool IsSubtitleFile(string path) =>
        Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
}
