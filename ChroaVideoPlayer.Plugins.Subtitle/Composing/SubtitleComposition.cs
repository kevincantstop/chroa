using System.Collections.Generic;

namespace ChroaVideoPlayer.Plugins.Subtitle;

public sealed class SubtitleComposition
{
    public SubtitleComposition(
        string title,
        string path,
        IReadOnlyList<ComposedSource> sources,
        ComposedSource editedSource)
    {
        Title = title;
        Path = path;
        Sources = sources;
        EditedSource = editedSource;
    }

    public string Title { get; }

    public string Path { get; }

    public IReadOnlyList<ComposedSource> Sources { get; }

    public ComposedSource EditedSource { get; }

    public bool IsMounted { get; set; }
}
