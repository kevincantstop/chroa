using ChroaVideoPlayer.Core.EventBus;
using ChroaVideoPlayer.Core.Plugin;
using ChroaVideoPlayer.Core.Plugin.UI;

namespace ChroaVideoPlayer.Plugins.Sample;

public sealed class SamplePlugin : IChroaPlugin, IChroaPluginEvents
{
    private string? _lastPlayed;

    private SamplePluginWindow? _window;

    public string Name => "sample";

    public string Label => "Sample";

    public string Icon => "avares://ChroaVideoPlayer.Plugins.Sample/Icons/sample.svg";

    public PluginLocation Location => PluginLocation.MainMenu;

    public void SubscribeEvents(IEventSubscriber events) =>
        events.Subscribe<VideoPlayed>(message =>
        {
            _lastPlayed = message.Path;

            _window?.ShowLastPlayed(message.Path);
        });

    public PluginWindow CreateWindow()
    {
        _window = new SamplePluginWindow();

        _window.ShowLastPlayed(_lastPlayed);

        _window.Closed += (_, _) => _window = null;

        return _window;
    }
}
