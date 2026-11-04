using System.Globalization;
using Microsoft.Data.Sqlite;
using Nordmann.LUI.Schema;
using Nordmann.LUI.Storage;

namespace Nordmann.LUI.Indexes;

/// <summary>
/// The stored indexes of a database: <c>lui_meta</c> and the statements that create and remove an index (LUI-Spezifikation
/// §7.1, §12.2). The builders describe an index; creating it, checking the stored definition and dropping it happens
/// here, in one transaction each (review DevEx D6: the DDL out of the builder).
/// </summary>
internal static class IndexStore
{
    /// <summary>Creates <c>lui_meta</c> if it is missing.</summary>
    internal static void Initialize(Database database)
    {
        using var connection = database.OpenConnection();
        database.Initialize(connection);

        using var meta = connection.CreateCommand();
        meta.CommandText = Meta.CreateTable;
        meta.ExecuteNonQuery();
    }

    /// <summary>
    /// Creates an index from the statements of its source, or checks that the stored definition is the same — in one
    /// transaction, so that two processes cannot both create it.
    /// </summary>
    /// <param name="database">The database.</param>
    /// <param name="source">The source of the index: its name, its definition, its statements.</param>
    /// <param name="createdAt">The time stored with a new index.</param>
    /// <param name="verify">A check of the environment of the index in the same transaction — for <c>Linked</c>, the
    /// application's table — that runs every time, not only when the index is created.</param>
    /// <exception cref="LuiDefinitionMismatchException">The index exists with another definition.</exception>
    internal static void Ensure(
        Database database,
        ContentSource source,
        DateTimeOffset createdAt,
        Action<SqliteConnection, SqliteTransaction>? verify = null)
    {
        using var scope = database.Begin();
        using var connection = database.OpenConnection();
        using var transaction = connection.BeginTransaction(deferred: false);

        verify?.Invoke(connection, transaction);

        var name = source.IndexName;
        var definition = source.Definition;
        var stored = ReadDefinition(connection, transaction, name);
        if (stored is null)
        {
            foreach (var statement in source.CreateStatements())
            {
                Execute(connection, transaction, statement);
            }

            WriteDefinition(connection, transaction, name, definition, createdAt);
        }
        else if (stored != definition)
        {
            throw new LuiDefinitionMismatchException(name, stored.ToJson(), definition.ToJson(), linked: stored.Link is not null);
        }

        transaction.Commit();
    }

    /// <summary>
    /// Removes an index with its tables, indexes and triggers and its row in <c>lui_meta</c>, whatever its stored
    /// definition is. The triggers of a linked index on the application's table go only with <paramref name="dropTriggers"/>.
    /// </summary>
    /// <returns><see langword="true"/> when the index existed.</returns>
    /// <exception cref="InvalidOperationException">A linked index has triggers, and <paramref name="dropTriggers"/> is not set.</exception>
    internal static bool Drop(Database database, string name, bool dropTriggers)
    {
        using var scope = database.Begin();
        using var connection = database.OpenConnection();
        using var transaction = connection.BeginTransaction(deferred: false);

        var stored = ReadDefinition(connection, transaction, name);
        var existed = stored is not null;

        if (!dropTriggers && stored?.Link is { } link && HasTriggers(connection, transaction, name))
        {
            throw new InvalidOperationException(
                $"The linked index '{name}' has triggers on the table '{link.Table}'. They write into the tables of the index "
                + "and go with it: call DropIndex(name, dropTriggers: true), and take them out of the application's migration "
                + "too. After a new Ensure() the index is empty and has no triggers — Sync() it and create them again.");
        }

        foreach (var statement in ContentSources.DropStatements(name, stored))
        {
            Execute(connection, transaction, statement);
        }

        using (var meta = connection.CreateCommand())
        {
            meta.Transaction = transaction;
            meta.CommandText = "DELETE FROM lui_meta WHERE index_name = $name;";
            meta.Parameters.AddWithValue("$name", name);
            meta.ExecuteNonQuery();
        }

        transaction.Commit();
        return existed;
    }

    /// <summary>The stored definition of an index, or <see langword="null"/> when there is none.</summary>
    internal static IndexDefinition? ReadDefinition(SqliteConnection connection, SqliteTransaction? transaction, string indexName)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT definition FROM lui_meta WHERE index_name = $name;";
        command.Parameters.AddWithValue("$name", indexName);

        return command.ExecuteScalar() is string json ? IndexDefinition.FromJson(json) : null;
    }

    private static void WriteDefinition(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string indexName,
        IndexDefinition definition,
        DateTimeOffset createdAt)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "INSERT INTO lui_meta(index_name, kind, source, definition, schema_version, created_at) "
            + "VALUES ($name, $kind, $source, $definition, $version, $created);";
        command.Parameters.AddWithValue("$name", indexName);
        command.Parameters.AddWithValue("$kind", definition.Kind.ToString());
        command.Parameters.AddWithValue("$source", definition.Source);
        command.Parameters.AddWithValue("$definition", definition.ToJson());
        command.Parameters.AddWithValue("$version", Meta.SchemaVersion);
        command.Parameters.AddWithValue("$created", createdAt.ToString("O", CultureInfo.InvariantCulture));
        command.ExecuteNonQuery();
    }

    private static bool HasTriggers(SqliteConnection connection, SqliteTransaction transaction, string indexName)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = LinkedSource.TriggerCountSql(indexName);
        return (long)command.ExecuteScalar()! > 0;
    }

    private static void Execute(SqliteConnection connection, SqliteTransaction transaction, string statement)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = statement;
        command.ExecuteNonQuery();
    }
}
