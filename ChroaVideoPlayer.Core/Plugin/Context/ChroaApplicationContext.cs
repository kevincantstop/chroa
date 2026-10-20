using ChroaVideoPlayer.Core.Mpv;

namespace ChroaVideoPlayer.Core.Plugin.Context;

public sealed class ChroaApplicationContext
{
    public ChroaApplicationContext(ChroaMpvContext mpv, IVideoOverlayHost? overlay = null)
    {
        Mpv = mpv;
        Overlay = overlay;
    }

    public ChroaMpvContext Mpv { get; }

    public IVideoOverlayHost? Overlay { get; }
}
