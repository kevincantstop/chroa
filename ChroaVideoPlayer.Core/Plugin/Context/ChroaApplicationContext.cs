using ChroaVideoPlayer.Core.Mpv;

namespace ChroaVideoPlayer.Core.Plugin.Context;

public sealed class ChroaApplicationContext
{
    public ChroaApplicationContext(
        ChroaMpvContext mpv,
        IVideoOverlayHost? overlay = null,
        IVideoControls? controls = null)
    {
        Mpv = mpv;
        Overlay = overlay;
        Controls = controls;
    }

    public ChroaMpvContext Mpv { get; }

    public IVideoOverlayHost? Overlay { get; }

    public IVideoControls? Controls { get; }
}
