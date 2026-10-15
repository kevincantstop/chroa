using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Microsoft.Data.Sqlite;

namespace ChroaVideoPlayer.Core.Persistance;

public sealed class LocalStore<T> where T : class, new()
{
    private static readonly TimeSpan DefaultSaveDelay = TimeSpan.FromSeconds(1);

    private readonly TableSchema _schema;
    private readonly TableRepository<T> _repository;
    private readonly DeferredSaveQueue _queue;
    private readonly LruLimiter<T>? _limiter;
    private readonly string _databasePath;
    private readonly string _connectionString;

    public LocalStore(string? databasePath = null, TimeSpan? saveDelay = null)
        : this(null, databasePath, saveDelay)
    {
    }

    public LocalStore(int limit, string recencyColumn, string? databasePath = null, TimeSpan? saveDelay = null)
        : this(new LruLimiter<T>(limit, recencyColumn), databasePath, saveDelay)
    {
    }

    private LocalStore(LruLimiter<T>? limiter, string? databasePath, TimeSpan? saveDelay)
    {
        _limiter = limiter;
        _schema = TableSchema.For<T>();
        _repository = new TableRepository<T>(_schema);

        _databasePath = databasePath ?? LocalDatabase.DefaultPath;
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
        }.ToString();

        _queue = new DeferredSaveQueue(saveDelay ?? DefaultSaveDelay, WritePendingValues);

        _limiter?.Trim(this);
    }

    public string TableName => _schema.Name;

    public bool Save(string key, T value)
    {
        RequireKey(key);
        if (value is null) throw new ArgumentNullException(nameof(value));

        var saved = Run(nameof(Save), connection => _repository.Save(connection, key, value), false);

        _limiter?.Trim(this);

        return saved;
    }

    public T? Get(string key)
    {
        RequireKey(key);

        return Run<T?>(nameof(Get), connection => _repository.Get(connection, key), null);
    }

    public bool Delete(string key)
    {
        RequireKey(key);

        return Run(nameof(Delete), connection => _repository.Delete(connection, key), false);
    }

    public bool Contains(string key)
    {
        RequireKey(key);

        return Run(nameof(Contains), connection => _repository.Contains(connection, key), false);
    }

    public IReadOnlyList<T> Query(string? search = null, string? orderBy = null, bool descending = false, int offset = 0, int limit = 50)
    {
        _repository.ValidateOrderBy(orderBy);

        if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
        if (limit < 1) throw new ArgumentOutOfRangeException(nameof(limit));

        return Run<IReadOnlyList<T>>(nameof(Query),
            connection => _repository.Query(connection, search, orderBy, descending, offset, limit),
            Array.Empty<T>());
    }

    public IReadOnlyList<string> Keys(string? search = null, string? orderBy = null, bool descending = false, int offset = 0, int limit = 50)
    {
        _repository.ValidateOrderBy(orderBy);

        if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
        if (limit < 1) throw new ArgumentOutOfRangeException(nameof(limit));

        return Run<IReadOnlyList<string>>(nameof(Keys),
            connection => _repository.Keys(connection, search, orderBy, descending, offset, limit),
            Array.Empty<string>());
    }

    public int Count(string? search = null) =>
        Run(nameof(Count), connection => _repository.Count(connection, search), 0);

    public void QueueSave(string key, T value)
    {
        RequireKey(key);
        if (value is null) throw new ArgumentNullException(nameof(value));

        _queue.Queue(key, _repository.CaptureValues(value));
    }

    public void Flush() => _queue.Flush();

    private TResult Run<TResult>(string operation, Func<SqliteConnection, TResult> action, TResult fallback)
    {
        try
        {
            using var connection = Open();
            TableCreator.EnsureCreated(connection, _schema);

            return action(connection);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"LocalStore<{typeof(T).Name}>.{operation} failed: {ex}");
            return fallback;
        }
    }

    private SqliteConnection Open()
    {
        var directory = Path.GetDirectoryName(_databasePath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    private void WritePendingValues(string key, IReadOnlyList<object?> values) =>
        _ = Run(nameof(WritePendingValues), connection => _repository.SaveValues(connection, key, values), false);

    private static void RequireKey(string key)
    {
        if (string.IsNullOrEmpty(key)) throw new ArgumentException("Key must not be empty.", nameof(key));
    }
}
