using System.Collections.Generic;
using Avalonia.Platform.Storage;

namespace ChroaVideoPlayer.Core.Utils;

public static class SubtitleFileTypes
{
    public static FilePickerFileType Subtitle { get; } = new("Subtitle Files")
    {
        Patterns = new[] { "*.srt", "*.ass", "*.ssa", "*.sub", "*.vtt", "*.sup", "*.idx", "*.lrc" },
        MimeTypes = new[] { "text/*" },
        AppleUniformTypeIdentifiers = new[] { "public.text" },
    };

    public static IReadOnlyList<FilePickerFileType> Filter { get; } = new[] { Subtitle, VideoFileTypes.All };
}
