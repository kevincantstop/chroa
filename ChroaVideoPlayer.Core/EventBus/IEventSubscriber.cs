using System;

namespace ChroaVideoPlayer.Core.EventBus;

public interface IEventSubscriber
{
    IDisposable Subscribe<TEvent>(Action<TEvent> handler);
}
