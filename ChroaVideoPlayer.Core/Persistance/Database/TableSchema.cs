using System;
using System.Collections.Generic;
using System.Reflection;
using ChroaVideoPlayer.Core.Persistance.Attributes;

namespace ChroaVideoPlayer.Core.Persistance.Database;

public sealed class TableSchema
{
    public const string KeyColumn = "key";

    private TableSchema(string name, IReadOnlyList<ColumnDefinition> columns)
    {
        Name = name;
        Columns = columns;
    }

    public string Name { get; }

    public IReadOnlyList<ColumnDefinition> Columns { get; }

    public static TableSchema For<T>() where T : class
    {
        var type = typeof(T);
        var columns = new List<ColumnDefinition>();

        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetCustomAttribute<PersistentColumnAttribute>() is null) continue;
            if (!property.CanRead || !property.CanWrite) continue;
            if (property.GetIndexParameters().Length > 0) continue;

            if (string.Equals(property.Name, KeyColumn, StringComparison.OrdinalIgnoreCase))
            {
                throw new NotSupportedException(
                    $"{type.Name}.{property.Name} clashes with the '{KeyColumn}' column; rename the property.");
            }

            var sqlType = SqliteTypeMap.ToSqlType(property.PropertyType)
                ?? throw new NotSupportedException(
                    $"{type.Name}.{property.Name} has unsupported type {property.PropertyType.Name}.");

            columns.Add(new ColumnDefinition(property, property.Name, sqlType));
        }

        if (columns.Count == 0)
        {
            throw new NotSupportedException(
                $"{type.Name} has no property marked with [PersistentColumn]; the table would have no columns.");
        }

        return new TableSchema(type.Name, columns);
    }
}

public sealed record ColumnDefinition(PropertyInfo Property, string Name, string SqlType);
