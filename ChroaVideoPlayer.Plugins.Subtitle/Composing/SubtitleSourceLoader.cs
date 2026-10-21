using System;
using System.Collections.Generic;
using System.IO;

namespace ChroaVideoPlayer.Plugins.Subtitle;

public static class SubtitleSourceLoader
{
    public static IReadOnlyList<ComposedSource> Load(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return Array.Empty<ComposedSource>();

        var extension = Path.GetExtension(path);

        if (extension.Equals(".ass", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".ssa", StringComparison.OrdinalIgnoreCase))
        {
            return AssParser.Load(path);
        }

        var cues = SrtParser.Load(path);

        return cues.Count == 0
            ? Array.Empty<ComposedSource>()
            : new[] { new ComposedSource($"file:{path}", Path.GetFileNameWithoutExtension(path), cues) };
    }
}
