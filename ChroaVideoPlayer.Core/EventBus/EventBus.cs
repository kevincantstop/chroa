using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace ChroaVideoPlayer.Core.EventBus;

public sealed class EventBus : IEventBus
{
    private readonly Dictionary<Type, List<object>> _handlers = new();

    private readonly object _gate = new();

    public IDisposable Subscribe<TEvent>(Action<TEvent> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        lock (_gate)
        {
            if (!_handlers.TryGetValue(typeof(TEvent), out var handlers))
            {
                handlers = new List<object>();

                _handlers[typeof(TEvent)] = handlers;
            }

            handlers.Add(handler);
        }

        return new Subscription(this, typeof(TEvent), handler);
    }

    public void Publish<TEvent>(TEvent message)
    {
        object[] handlers;

        lock (_gate)
        {
            if (!_handlers.TryGetValue(typeof(TEvent), out var registered)) return;

            handlers = registered.ToArray();
        }

        foreach (var handler in handlers)
        {
            try
            {
                ((Action<TEvent>)handler)(message);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"An event handler for {typeof(TEvent).Name} failed: {ex.Message}");
            }
        }
    }

    private void Remove(Type eventType, object handler)
    {
        lock (_gate)
        {
            if (!_handlers.TryGetValue(eventType, out var handlers)) return;

            handlers.Remove(handler);

            if (handlers.Count == 0) _handlers.Remove(eventType);
        }
    }

    private sealed class Subscription : IDisposable
    {
        private readonly Type _eventType;

        private readonly object _handler;

        private EventBus? _bus;

        public Subscription(EventBus bus, Type eventType, object handler)
        {
            _bus = bus;
            _eventType = eventType;
            _handler = handler;
        }

        public void Dispose()
        {
            var bus = _bus;

            if (bus is null) return;

            _bus = null;

            bus.Remove(_eventType, _handler);
        }
    }
}
