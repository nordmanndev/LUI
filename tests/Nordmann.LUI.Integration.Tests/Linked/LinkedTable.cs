using Microsoft.Data.Sqlite;

namespace Nordmann.LUI.Integration.Tests.Linked;

/// <summary>
/// The table of the application that the tests of a <c>Linked</c> index run against, and the few statements they write to
/// it with. The tests of the linked index share it: one table, spelled once.
/// </summary>
internal static class LinkedTable
{
    internal const string Table = """
        CREATE TABLE tag (
            id          INTEGER PRIMARY KEY,
            name        TEXT,
            usage_count INTEGER NOT NULL DEFAULT 0,
            priority    INTEGER,
            slug        TEXT,
            color       TEXT);
        """;

    internal static ILuiLinkedIndex Tags(LuiCatalog catalog) =>
        catalog.CreateLinked("tags", index => index.SingleWord().LinkTo("tag", "id", "name", weight: "usage_count"));

    internal static void Insert(TestDatabase database, params (long Id, string? Name, int Weight)[] rows)
    {
        using var connection = database.Connect();
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO tag(id, name, usage_count) VALUES ($id, $name, $weight);";
        var id = command.Parameters.Add("$id", SqliteType.Integer);
        var name = command.Parameters.Add("$name", SqliteType.Text);
        var weight = command.Parameters.Add("$weight", SqliteType.Integer);
        foreach (var row in rows)
        {
            id.Value = row.Id;
            name.Value = (object?)row.Name ?? DBNull.Value;
            weight.Value = row.Weight;
            command.ExecuteNonQuery();
        }
    }

    internal static string Schema(TestDatabase database, string table)
    {
        using var connection = database.Connect();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT group_concat(type || ' ' || name || ' ' || coalesce(sql, ''), '\n') FROM sqlite_master WHERE tbl_name = $table;";
        command.Parameters.AddWithValue("$table", table);
        return (string)command.ExecuteScalar()!;
    }

    internal static void Run(TestDatabase database, string sql)
    {
        using var connection = database.Connect();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    internal static void Run(SqliteConnection connection, SqliteTransaction transaction, string sql)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
