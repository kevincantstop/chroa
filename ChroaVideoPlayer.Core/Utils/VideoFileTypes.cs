using System.Collections.Generic;
using Avalonia.Platform.Storage;

namespace ChroaVideoPlayer.Core.Utils;

public static class VideoFileTypes
{
    public static FilePickerFileType Video { get; } = new("Video Files")
    {
        Patterns = new[]
        {
            "*.mp4", "*.mkv", "*.mov", "*.m4v", "*.avi", "*.webm", "*.flv",
            "*.wmv", "*.ts", "*.m2ts", "*.mpg", "*.mpeg", "*.rmvb", "*.3gp",
        },
        MimeTypes = new[] { "video/*" },
        AppleUniformTypeIdentifiers = new[] { "public.movie" },
    };

    public static FilePickerFileType All { get; } = new("All Files")
    {
        Patterns = new[] { "*.*" },
    };

    public static IReadOnlyList<FilePickerFileType> Filter { get; } = new[] { Video, All };
}
