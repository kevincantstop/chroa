using ChroaVideoPlayer.Core.Mpv;

namespace ChroaVideoPlayer.Core.Plugin.Context;

public sealed class ChroaApplicationContext
{
    public ChroaApplicationContext(ChroaMpvContext mpv) => Mpv = mpv;

    public ChroaMpvContext Mpv { get; }
}
