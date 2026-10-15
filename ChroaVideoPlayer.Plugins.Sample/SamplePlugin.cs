using ChroaVideoPlayer.Core.Plugin;
using ChroaVideoPlayer.Core.Plugin.UI;

namespace ChroaVideoPlayer.Plugins.Sample;

public sealed class SamplePlugin : IChroaPlugin
{
    public string Name => "sample";

    public string Label => "Sample";

    public string Icon => "avares://ChroaVideoPlayer.Plugins.Sample/Icons/sample.svg";

    public PluginLocation Location => PluginLocation.MainMenu;

    public PluginWindow CreateWindow() => new SamplePluginWindow();
}
