namespace Nordmann.LUI.Schema;

/// <summary>Comparisons of SQL as SQLite stores it.</summary>
internal static class SqlText
{
    /// <summary>Two statements are the same when they differ only in white space and a closing semicolon.</summary>
    internal static bool Same(string stored, string expected) =>
        string.Equals(Collapse(stored), Collapse(expected), StringComparison.Ordinal);

    private static string Collapse(string sql) =>
        string.Join(' ', sql.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).TrimEnd(';').TrimEnd();
}
