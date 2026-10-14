using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Data.Sqlite;

namespace ChroaVideoPlayer.Core.Persistance;

public static class TableCreator
{
    public static bool Exists(SqliteConnection connection, TableSchema schema)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = @name LIMIT 1";
        command.Parameters.AddWithValue("@name", schema.Name);

        return command.ExecuteScalar() is not null;
    }

    public static void EnsureCreated(SqliteConnection connection, TableSchema schema)
    {
        CreateTable(connection, schema);
        AddMissingColumns(connection, schema);
    }

    private static void CreateTable(SqliteConnection connection, TableSchema schema)
    {
        var parts = new List<string> { $"{SqliteSyntax.Quote(TableSchema.KeyColumn)} TEXT PRIMARY KEY" };
        parts.AddRange(schema.Columns.Select(c => $"{SqliteSyntax.Quote(c.Name)} {c.SqlType}"));

        using var command = connection.CreateCommand();
        command.CommandText = $"CREATE TABLE IF NOT EXISTS {SqliteSyntax.Quote(schema.Name)} ({string.Join(", ", parts)})";
        command.ExecuteNonQuery();
    }

    private static void AddMissingColumns(SqliteConnection connection, TableSchema schema)
    {
        var existing = GetColumnNames(connection, schema.Name);

        foreach (var column in schema.Columns)
        {
            if (existing.Contains(column.Name)) continue;

            using var command = connection.CreateCommand();
            command.CommandText =
                $"ALTER TABLE {SqliteSyntax.Quote(schema.Name)} ADD COLUMN {SqliteSyntax.Quote(column.Name)} {column.SqlType}";
            command.ExecuteNonQuery();
        }
    }

    private static HashSet<string> GetColumnNames(SqliteConnection connection, string table)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({SqliteSyntax.Quote(table)})";

        using var reader = command.ExecuteReader();
        while (reader.Read()) names.Add(reader.GetString(1));

        return names;
    }
}
