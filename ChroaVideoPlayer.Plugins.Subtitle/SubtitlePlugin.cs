using ChroaVideoPlayer.Core.Plugin;
using ChroaVideoPlayer.Core.Plugin.Context;
using ChroaVideoPlayer.Core.Plugin.UI;

namespace ChroaVideoPlayer.Plugins.Subtitle;

public sealed class SubtitlePlugin : IChroaPlugin
{
    public string Name => "subtitle";

    public string Label => "Subtitle Editor";

    public string Icon => "avares://ChroaVideoPlayer.Plugins.Subtitle/Icons/captions.svg";

    public PluginLocation Location => PluginLocation.MainMenu;

    public bool Enabled => true;

    public ChroaApplicationContext? Context { get; set; }

    public PluginWindow CreateWindow() => new SubtitleWindow();
}
