using Microsoft.Data.Sqlite;

namespace Nordmann.LUI.Sample.Data;

/// <summary>
/// The application's own table, which the <c>tags</c> index is linked to. It is the application's, not LUI's: LUI reads
/// it and never changes it (ADR-0015); the triggers that tell the index about a change are created by the application,
/// on purpose, by <see cref="Indexes.LinkedSampleIndex{TPayload}"/>.
/// </summary>
internal static class SampleDatabase
{
    internal const string TagTable = "tag";

    /// <summary>Creates the <c>tag</c> table and fills it once. Runs before the host starts: an index linked to a table needs the table.</summary>
    internal static void Initialize(string connectionString)
    {
        using var connection = new SqliteConnection(connectionString);
        connection.Open();

        using (var create = connection.CreateCommand())
        {
            create.CommandText = $"""
                CREATE TABLE IF NOT EXISTS {TagTable} (
                    id          INTEGER PRIMARY KEY,
                    name        TEXT    NOT NULL,
                    usage_count INTEGER NOT NULL DEFAULT 0,
                    rank        INTEGER NOT NULL DEFAULT 3,
                    kind        TEXT    NOT NULL DEFAULT 'Sonstiges',
                    color       TEXT    NOT NULL DEFAULT '#718096'
                );
                """;
            create.ExecuteNonQuery();
        }

        using var count = connection.CreateCommand();
        count.CommandText = $"SELECT COUNT(*) FROM {TagTable}";
        if ((long)count.ExecuteScalar()! > 0)
        {
            return;
        }

        using var transaction = connection.BeginTransaction();
        foreach (var (name, kind, color, usage, rank) in TagData.All)
        {
            using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = $"INSERT INTO {TagTable} (name, usage_count, rank, kind, color) VALUES ($name, $usage, $rank, $kind, $color)";
            insert.Parameters.AddWithValue("$name", name);
            insert.Parameters.AddWithValue("$usage", usage);
            insert.Parameters.AddWithValue("$rank", rank);
            insert.Parameters.AddWithValue("$kind", kind);
            insert.Parameters.AddWithValue("$color", color);
            insert.ExecuteNonQuery();
        }

        transaction.Commit();
    }
}
