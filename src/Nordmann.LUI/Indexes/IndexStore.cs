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
    /// <summary>The one statement of the read path: what <c>lui_meta</c> holds about an index (ADR-0033).</summary>
    private const string ReadSql =
        "SELECT kind, source, definition, schema_version FROM lui_meta WHERE index_name = $name;";

    /// <summary>
    /// Creates <c>lui_meta</c> if it is missing — and switches the file to write-ahead logging.
    /// <para>
    /// A file that may not be written gets neither (ADR-0033): a read-only catalog creates no table and writes no
    /// pragma. The table has to be there — the writer made it — and if it is not, the catalog says so.
    /// </para>
    /// </summary>
    internal static void Initialize(Database database)
    {
        if (database.ReadOnly)
        {
            EnsureMetaTable(database);
            return;
        }

        using var connection = database.OpenConnection();
        database.Initialize(connection);

        using var meta = connection.CreateCommand();
        meta.CommandText = Meta.CreateTable;
        meta.ExecuteNonQuery();
    }

    /// <summary>The table of the library has to be there: a read-only catalog cannot create it.</summary>
    /// <exception cref="LuiReadOnlyException">The file has no <c>lui_meta</c>.</exception>
    private static void EnsureMetaTable(Database database)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM sqlite_master WHERE type = 'table' AND name = 'lui_meta';";

        if ((long)command.ExecuteScalar()! == 0)
        {
            throw new LuiReadOnlyException(
                "opening the catalog",
                "The file has no table 'lui_meta': a read-only catalog cannot create it. Create the index with a "
                + "writable catalog first (LuiCatalog.Open(\"Data Source=app.db\") and Create(name, …)), or open the "
                + "file that the writer wrote.");
        }
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
    /// <exception cref="LuiReadOnlyException">The file may not be written.</exception>
    internal static void Create(
        Database database,
        ContentSource source,
        DateTimeOffset createdAt,
        Action<SqliteConnection, SqliteTransaction>? verify = null)
    {
        database.ThrowIfReadOnly("creating an index");

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
    /// <exception cref="LuiReadOnlyException">The file may not be written.</exception>
    internal static bool Drop(Database database, string name, bool dropTriggers)
    {
        database.ThrowIfReadOnly("dropping an index");

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
                + "too. After a new Create() the index is empty and has no triggers — Sync() it and create them again.");
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

    /// <summary>
    /// The stored index of a name — its kind, its content source, its definition and the version of the schema it was
    /// written with — or <see langword="null"/> when there is no such index.
    /// <para>
    /// It reads with a plain <c>SELECT</c> on a connection of its own: no transaction, no write lock, no table
    /// (ADR-0033). A reader that must not write — a replica, a file that is read-only, a service that may not block the
    /// writer — comes through here.
    /// </para>
    /// </summary>
    /// <param name="database">The database.</param>
    /// <param name="indexName">The name of the index.</param>
    /// <returns>What <c>lui_meta</c> holds, or <see langword="null"/> when there is no such index.</returns>
    internal static IndexStoreEntry? ReadIndex(Database database, string indexName)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = ReadSql;
        command.Parameters.AddWithValue("$name", indexName);

        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        var kind = ReadKind(reader.GetString(0), indexName);
        var source = reader.GetString(1);
        var definition = IndexDefinition.FromJson(reader.GetString(2))
            ?? throw new InvalidOperationException(
                $"The definition of the index '{indexName}' in lui_meta is empty; the file was written by another version "
                + "of LUI and cannot be read: remove the index (LuiCatalog.DropIndex) and create it again.");
        var schemaVersion = reader.GetInt32(3);

        return new IndexStoreEntry(kind, source, definition, schemaVersion);
    }

    /// <summary>The kind of the stored index; a kind this version does not know is a file of another version.</summary>
    private static LuiIndexKind ReadKind(string kind, string indexName) =>
        Enum.TryParse(kind, out LuiIndexKind parsed)
            ? parsed
            : throw new LuiDefinitionMismatchException(
                indexName,
                $"the kind {kind}",
                $"a kind of this version ({string.Join(", ", Enum.GetNames<LuiIndexKind>())})",
                "The index was written by another version of LUI: remove it with LuiCatalog.DropIndex(name) and create "
                + "it again.");

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
