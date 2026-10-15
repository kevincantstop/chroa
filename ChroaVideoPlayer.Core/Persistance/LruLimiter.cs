using System;

namespace ChroaVideoPlayer.Core.Persistance;

internal sealed class LruLimiter<T> where T : class, new()
{
    private const int BatchSize = 50;

    private readonly int _limit;
    private readonly string _recencyColumn;

    public LruLimiter(int limit, string recencyColumn)
    {
        _limit = Math.Max(1, limit);
        _recencyColumn = recencyColumn;
    }

    public void Trim(LocalStore<T> store)
    {
        while (true)
        {
            var stale = store.Keys(orderBy: _recencyColumn, descending: true, offset: _limit, limit: BatchSize);
            if (stale.Count == 0) return;

            foreach (var key in stale) store.Delete(key);
        }
    }
}
