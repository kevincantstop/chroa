namespace ChroaVideoPlayer.Core.EventBus;

public interface IEventBus : IEventSubscriber
{
    void Publish<TEvent>(TEvent message);
}
