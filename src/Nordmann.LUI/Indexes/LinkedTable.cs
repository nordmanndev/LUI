using Microsoft.Data.Sqlite;
using Nordmann.LUI.Schema;

namespace Nordmann.LUI.Indexes;

/// <summary>
/// Checks, when a <c>Linked</c> index is ensured, that the application's table is what the link says — and changes
/// nothing (ADR-0015): the table, its columns, and a key that is its <c>INTEGER PRIMARY KEY</c>, the rowid of the
/// table and with it the document id of the index.
/// </summary>
internal static class LinkedTable
{
    internal static void Verify(SqliteConnection connection, SqliteTransaction transaction, LinkDefinition link, IReadOnlyList<string> payloadColumns)
    {
        var columns = Columns(connection, transaction, link.Table);
        if (columns.Count == 0)
        {
            throw new LuiLinkedTableException($"The table '{link.Table}' that the index is linked to is not there.");
        }

        if (!columns.TryGetValue(link.Key, out var key) || key.PrimaryKey != 1
            || columns.Values.Count(column => column.PrimaryKey > 0) != 1
            || !string.Equals(key.Type, "INTEGER", StringComparison.OrdinalIgnoreCase)
            || WithoutRowId(connection, transaction, link.Table))
        {
            throw new LuiLinkedTableException(
                $"The key '{link.Key}' of the table '{link.Table}' must be its INTEGER PRIMARY KEY — the rowid of the table, "
                + "which is the document id of the index. A table with another key can be indexed as Owned with "
                + "WithKey<string>().");
        }

        string[] named = [link.Text, .. new[] { link.Weight, link.Priority }.OfType<string>(), .. payloadColumns];
        var missing = named.Where(column => !columns.ContainsKey(column)).ToArray();
        if (missing.Length > 0)
        {
            throw new LuiLinkedTableException(
                $"The table '{link.Table}' has no column {string.Join(", ", missing.Select(column => $"'{column}'"))}.");
        }
    }

    private static Dictionary<string, (string Type, long PrimaryKey)> Columns(
        SqliteConnection connection, SqliteTransaction transaction, string table)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT name, type, pk FROM pragma_table_info($table);";
        command.Parameters.AddWithValue("$table", table);

        var columns = new Dictionary<string, (string, long)>(StringComparer.OrdinalIgnoreCase);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            columns[reader.GetString(0)] = (reader.GetString(1), reader.GetInt64(2));
        }

        return columns;
    }

    private static bool WithoutRowId(SqliteConnection connection, SqliteTransaction transaction, string table)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT wr FROM pragma_table_list WHERE schema = 'main' AND name = $table;";
        command.Parameters.AddWithValue("$table", table);
        return command.ExecuteScalar() is long withoutRowId && withoutRowId != 0;
    }
}
