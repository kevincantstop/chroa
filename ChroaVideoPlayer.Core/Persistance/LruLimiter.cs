using System;
using System.Collections.Generic;

namespace ChroaVideoPlayer.Core.Persistance;

public sealed class LruLimiter<T> where T : class, new()
{
    private const int BatchSize = 50;

    private readonly LocalStore<T> _store;
    private readonly string _recencyColumn;

    public LruLimiter(LocalStore<T> store, string recencyColumn)
    {
        _store = store;
        _recencyColumn = recencyColumn;
    }

    public void Trim(int keep)
    {
        var limit = Math.Max(1, keep);

        while (true)
        {
            var stale = _store.Keys(orderBy: _recencyColumn, descending: true, offset: limit, limit: BatchSize);
            if (stale.Count == 0) return;

            foreach (var key in stale) _store.Delete(key);
        }
    }
}
