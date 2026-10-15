using ChroaVideoPlayer.Core.EventBus;

namespace ChroaVideoPlayer.Core.Plugin;

public interface IChroaPluginEvents
{
    void SubscribeEvents(IEventSubscriber events);
}
