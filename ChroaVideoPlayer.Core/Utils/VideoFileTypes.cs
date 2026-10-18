using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Platform.Storage;

namespace ChroaVideoPlayer.Core.Utils;

public static class VideoFileTypes
{
    public static IReadOnlyList<string> Extensions { get; } = new[]
    {
        ".mp4", ".mkv", ".mov", ".m4v", ".avi", ".webm", ".flv",
        ".wmv", ".ts", ".m2ts", ".mpg", ".mpeg", ".rmvb", ".3gp",
    };

    public static FilePickerFileType Video { get; } = new("Video Files")
    {
        Patterns = Extensions.Select(extension => "*" + extension).ToArray(),
        MimeTypes = new[] { "video/*" },
        AppleUniformTypeIdentifiers = new[] { "public.movie" },
    };

    public static FilePickerFileType All { get; } = new("All Files")
    {
        Patterns = new[] { "*.*" },
    };

    public static IReadOnlyList<FilePickerFileType> Filter { get; } = new[] { Video, All };

    public static bool IsVideoFile(string path) =>
        Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
}
