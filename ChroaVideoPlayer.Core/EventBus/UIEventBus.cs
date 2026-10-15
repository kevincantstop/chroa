using System;
using Avalonia.Threading;

namespace ChroaVideoPlayer.Core.EventBus;

public sealed class UIEventBus : IEventBus
{
    private readonly IEventBus _bus;

    public UIEventBus(IEventBus bus) => _bus = bus;

    public IDisposable Subscribe<TEvent>(Action<TEvent> handler) => _bus.Subscribe(handler);

    public void Publish<TEvent>(TEvent message)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            _bus.Publish(message);

            return;
        }

        Dispatcher.UIThread.Post(() => _bus.Publish(message));
    }
}
