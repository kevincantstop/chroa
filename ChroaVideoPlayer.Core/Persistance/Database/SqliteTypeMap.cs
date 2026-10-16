using System;
using System.Globalization;

namespace ChroaVideoPlayer.Core.Persistance.Database;

public static class SqliteTypeMap
{
    public static string? ToSqlType(Type type)
    {
        var target = Nullable.GetUnderlyingType(type) ?? type;

        if (target == typeof(string)) return "TEXT";
        if (target == typeof(bool)) return "INTEGER";
        if (target.IsEnum) return "INTEGER";
        if (target == typeof(byte) || target == typeof(sbyte) ||
            target == typeof(short) || target == typeof(ushort) ||
            target == typeof(int) || target == typeof(uint) ||
            target == typeof(long) || target == typeof(ulong)) return "INTEGER";
        if (target == typeof(float) || target == typeof(double)) return "REAL";
        if (target == typeof(decimal)) return "TEXT";
        if (target == typeof(DateTime) || target == typeof(DateTimeOffset)) return "TEXT";
        if (target == typeof(TimeSpan)) return "INTEGER";
        if (target == typeof(Guid)) return "TEXT";

        return null;
    }

    public static object ToDb(object? value) => value switch
    {
        null => DBNull.Value,
        bool flag => flag ? 1L : 0L,
        string text => text,
        Enum enumeration => Convert.ToInt64(enumeration, CultureInfo.InvariantCulture),
        byte or sbyte or short or ushort or int or uint or long or ulong => Convert.ToInt64(value, CultureInfo.InvariantCulture),
        float or double => Convert.ToDouble(value, CultureInfo.InvariantCulture),
        decimal number => number.ToString(CultureInfo.InvariantCulture),
        DateTime time => time.ToString("O", CultureInfo.InvariantCulture),
        DateTimeOffset time => time.ToString("O", CultureInfo.InvariantCulture),
        TimeSpan span => span.Ticks,
        Guid id => id.ToString("D"),
        _ => throw new NotSupportedException($"Unsupported value type {value.GetType().Name}."),
    };

    public static object? FromDb(object value, Type type)
    {
        var target = Nullable.GetUnderlyingType(type) ?? type;

        if (target == typeof(string)) return Convert.ToString(value, CultureInfo.InvariantCulture);
        if (target == typeof(bool)) return Convert.ToInt64(value, CultureInfo.InvariantCulture) != 0;
        if (target == typeof(Guid)) return Guid.Parse(Convert.ToString(value, CultureInfo.InvariantCulture)!);
        if (target == typeof(TimeSpan)) return TimeSpan.FromTicks(Convert.ToInt64(value, CultureInfo.InvariantCulture));
        if (target == typeof(decimal)) return decimal.Parse(Convert.ToString(value, CultureInfo.InvariantCulture)!, CultureInfo.InvariantCulture);
        if (target == typeof(DateTime)) return DateTime.Parse(Convert.ToString(value, CultureInfo.InvariantCulture)!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        if (target == typeof(DateTimeOffset)) return DateTimeOffset.Parse(Convert.ToString(value, CultureInfo.InvariantCulture)!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        if (target.IsEnum) return Enum.ToObject(target, Convert.ToInt64(value, CultureInfo.InvariantCulture));

        return Convert.ChangeType(value, target, CultureInfo.InvariantCulture);
    }
}
