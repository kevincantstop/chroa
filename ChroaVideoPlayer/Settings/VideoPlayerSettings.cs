using System;

namespace ChroaVideoPlayer.Settings;

public sealed class VideoPlayerSettings
{
    public double Volume { get; set; } = 100;

    public bool AlwaysOnTop { get; set; }

    public double WindowWidth { get; set; }

    public double WindowHeight { get; set; }

    public DateTime LastOpenedUtc { get; set; }
}
