namespace ChroaVideoPlayer.Core.Persistance.Database;

public static class SqliteSyntax
{
    public static string Quote(string identifier) => "\"" + identifier.Replace("\"", "\"\"") + "\"";

    public static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}
