using ChroaVideoPlayer.Core.Plugin.UI;

namespace ChroaVideoPlayer.Core.Plugin;

public interface IChroaPlugin
{
    string Name { get; }

    string Label { get; }

    string Icon { get; }

    PluginLocation Location { get; }

    PluginWindow CreateWindow();
}
