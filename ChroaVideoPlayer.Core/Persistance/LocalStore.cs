using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Microsoft.Data.Sqlite;

namespace ChroaVideoPlayer.Core.Persistance;

public sealed class LocalStore<T> where T : class, new()
{
    private readonly TableSchema _schema;
    private readonly TableRepository<T> _repository;
    private readonly string _databasePath;
    private readonly string _connectionString;

    public LocalStore(string? databasePath = null)
    {
        _schema = TableSchema.For<T>();
        _repository = new TableRepository<T>(_schema);

        _databasePath = databasePath ?? LocalDatabase.DefaultPath;
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
        }.ToString();
    }

    public string TableName => _schema.Name;

    public bool Save(string key, T value)
    {
        RequireKey(key);
        if (value is null) throw new ArgumentNullException(nameof(value));

        return Run(nameof(Save), connection => _repository.Save(connection, key, value), false);
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

    public int Count(string? search = null) =>
        Run(nameof(Count), connection => _repository.Count(connection, search), 0);

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

    private static void RequireKey(string key)
    {
        if (string.IsNullOrEmpty(key)) throw new ArgumentException("Key must not be empty.", nameof(key));
    }
}
