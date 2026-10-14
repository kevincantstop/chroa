using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.Data.Sqlite;

namespace ChroaVideoPlayer.Core.Persistance;

public sealed class TableRepository<T> where T : class, new()
{
    private readonly TableSchema _schema;

    public TableRepository(TableSchema schema) => _schema = schema;

    public bool Save(SqliteConnection connection, string key, T value) =>
        SaveValues(connection, key, CaptureValues(value));

    public IReadOnlyList<object?> CaptureValues(T value)
    {
        var values = new object?[_schema.Columns.Count];

        for (var i = 0; i < _schema.Columns.Count; i++)
        {
            values[i] = SqliteTypeMap.ToDb(_schema.Columns[i].Property.GetValue(value));
        }

        return values;
    }

    public bool SaveValues(SqliteConnection connection, string key, IReadOnlyList<object?> values)
    {
        if (values.Count != _schema.Columns.Count)
        {
            throw new ArgumentException($"Expected {_schema.Columns.Count} values for {_schema.Name}.", nameof(values));
        }

        using var command = connection.CreateCommand();
        command.Parameters.AddWithValue("@key", key);

        var names = string.Join(", ", _schema.Columns.Select(c => SqliteSyntax.Quote(c.Name)));
        var placeholders = string.Join(", ", _schema.Columns.Select((_, i) => "@v" + i));
        var updates = string.Join(", ", _schema.Columns.Select(c => $"{SqliteSyntax.Quote(c.Name)} = excluded.{SqliteSyntax.Quote(c.Name)}"));

        for (var i = 0; i < _schema.Columns.Count; i++)
        {
            command.Parameters.AddWithValue("@v" + i, values[i] ?? DBNull.Value);
        }

        command.CommandText =
            $"INSERT INTO {SqliteSyntax.Quote(_schema.Name)} ({SqliteSyntax.Quote(TableSchema.KeyColumn)}, {names}) " +
            $"VALUES (@key, {placeholders}) " +
            $"ON CONFLICT({SqliteSyntax.Quote(TableSchema.KeyColumn)}) DO UPDATE SET {updates}";

        return command.ExecuteNonQuery() > 0;
    }

    public T? Get(SqliteConnection connection, string key)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {SelectList} FROM {SqliteSyntax.Quote(_schema.Name)} WHERE {KeyFilter} LIMIT 1";
        command.Parameters.AddWithValue("@key", key);

        using var reader = command.ExecuteReader();
        return reader.Read() ? Materialize(reader) : null;
    }

    public bool Delete(SqliteConnection connection, string key)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"DELETE FROM {SqliteSyntax.Quote(_schema.Name)} WHERE {KeyFilter}";
        command.Parameters.AddWithValue("@key", key);

        return command.ExecuteNonQuery() > 0;
    }

    public bool Contains(SqliteConnection connection, string key)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT 1 FROM {SqliteSyntax.Quote(_schema.Name)} WHERE {KeyFilter} LIMIT 1";
        command.Parameters.AddWithValue("@key", key);

        return command.ExecuteScalar() is not null;
    }

    public IReadOnlyList<T> Query(SqliteConnection connection, string? search, string? orderBy, bool descending, int offset, int limit)
    {
        using var command = connection.CreateCommand();

        var where = BuildSearch(command, search);
        var order = string.IsNullOrEmpty(orderBy)
            ? string.Empty
            : $" ORDER BY {SqliteSyntax.Quote(orderBy)} {(descending ? "DESC" : "ASC")}";

        command.Parameters.AddWithValue("@limit", limit);
        command.Parameters.AddWithValue("@offset", offset);
        command.CommandText =
            $"SELECT {SelectList} FROM {SqliteSyntax.Quote(_schema.Name)}{where}{order} LIMIT @limit OFFSET @offset";

        var result = new List<T>();

        using var reader = command.ExecuteReader();
        while (reader.Read()) result.Add(Materialize(reader));

        return result;
    }

    public int Count(SqliteConnection connection, string? search)
    {
        using var command = connection.CreateCommand();

        var where = BuildSearch(command, search);
        command.CommandText = $"SELECT COUNT(*) FROM {SqliteSyntax.Quote(_schema.Name)}{where}";

        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    public void ValidateOrderBy(string? orderBy)
    {
        if (string.IsNullOrEmpty(orderBy)) return;
        if (string.Equals(orderBy, TableSchema.KeyColumn, StringComparison.OrdinalIgnoreCase)) return;
        if (_schema.Columns.Any(c => string.Equals(c.Name, orderBy, StringComparison.OrdinalIgnoreCase))) return;

        throw new ArgumentException($"Unknown column '{orderBy}' on {_schema.Name}.", nameof(orderBy));
    }

    private string SelectList => string.Join(", ", _schema.Columns.Select(c => SqliteSyntax.Quote(c.Name)));

    private static string KeyFilter => $"{SqliteSyntax.Quote(TableSchema.KeyColumn)} = @key";

    private string BuildSearch(SqliteCommand command, string? search)
    {
        if (string.IsNullOrWhiteSpace(search)) return string.Empty;

        var textColumns = _schema.Columns.Where(c => c.SqlType == "TEXT").ToList();
        if (textColumns.Count == 0) return " WHERE 1 = 0";

        command.Parameters.AddWithValue("@search", "%" + SqliteSyntax.EscapeLike(search) + "%");

        var clauses = textColumns.Select(c => $"{SqliteSyntax.Quote(c.Name)} LIKE @search ESCAPE '\\'");
        return $" WHERE ({string.Join(" OR ", clauses)})";
    }

    private T Materialize(SqliteDataReader reader)
    {
        var item = new T();

        for (var i = 0; i < _schema.Columns.Count; i++)
        {
            if (reader.IsDBNull(i)) continue;

            var column = _schema.Columns[i];
            column.Property.SetValue(item, SqliteTypeMap.FromDb(reader.GetValue(i), column.Property.PropertyType));
        }

        return item;
    }
}
