using System;
using System.Collections.Generic;
using System.Threading;

namespace ChroaVideoPlayer.Core.Persistance;

public sealed class DeferredSaveQueue
{
    private readonly TimeSpan _delay;
    private readonly Action<string, IReadOnlyList<object?>> _write;
    private readonly Dictionary<string, IReadOnlyList<object?>> _pending = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    private Timer? _timer;

    public DeferredSaveQueue(TimeSpan delay, Action<string, IReadOnlyList<object?>> write)
    {
        _delay = delay;
        _write = write;
    }

    public void Queue(string key, IReadOnlyList<object?> values)
    {
        lock (_gate)
        {
            _pending[key] = values;

            if (_timer is null) _timer = new Timer(_ => Flush());

            _timer.Change(_delay, Timeout.InfiniteTimeSpan);
        }
    }

    public void Flush()
    {
        var batch = Take();

        foreach (var entry in batch) _write(entry.Key, entry.Value);
    }

    private List<KeyValuePair<string, IReadOnlyList<object?>>> Take()
    {
        lock (_gate)
        {
            _timer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

            var batch = new List<KeyValuePair<string, IReadOnlyList<object?>>>(_pending.Count);

            foreach (var entry in _pending) batch.Add(entry);

            _pending.Clear();

            return batch;
        }
    }
}
