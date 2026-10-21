using ChroaVideoPlayer.Core.Plugin;
using ChroaVideoPlayer.Core.Plugin.Context;
using ChroaVideoPlayer.Core.Plugin.UI;

namespace ChroaVideoPlayer.Plugins.Subtitle;

public sealed class SubtitlePlugin : IChroaPlugin
{
    public string Name => "subtitle";

    public string Label => "Video Subtitles";

    public string Icon => "avares://ChroaVideoPlayer.Plugins.Subtitle/Icons/captions.svg";

    public PluginLocation Location => PluginLocation.MainMenu;

    public bool Enabled => true;

    public ChroaApplicationContext? Context { get; set; }

    public PluginWindow CreateWindow()
    {
        var window = new SubtitleWindow();

        if (Context is { } context) window.Attach(context.Mpv);

        return window;
    }
}
