using System;
using ChroaVideoPlayer.Core.Persistance.Attributes;

namespace ChroaVideoPlayer.Settings;

public sealed class VideoPlayerSettings
{
    [PersistentColumn]
    public double Volume { get; set; } = 100;

    [PersistentColumn]
    public bool AlwaysOnTop { get; set; }

    [PersistentColumn]
    public double WindowWidth { get; set; }

    [PersistentColumn]
    public double WindowHeight { get; set; }

    [PersistentColumn]
    public DateTime LastOpenedUtc { get; set; }
}
