using Nordmann.LUI.Storage;
using Shouldly;

namespace Nordmann.LUI.Integration.Tests.Catalog;

/// <summary>
/// SQLite's schema counter (ADR-0075, issue #265) is what <c>PRAGMA schema_version</c> reads: a number in the header of the
/// file that changes whenever a table, an index or a trigger is created or dropped, and not when content is written. It is
/// <b>not</b> the LUI format version of <c>lui_meta.schema_version</c> (ADR-0073), which says in which format an index was
/// written. The catalog compares the counter with the one it last saw to know whether its definitions in memory may be stale.
/// </summary>
public sealed class SchemaCounterTests
{
    private static readonly LuiIndexDefinition<long> Tags = new("tags", index => index.SingleWord());

    [Fact]
    public void The_schema_counter_changes_with_the_schema_and_not_with_the_content()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var tags = catalog.Create(Tags);

        var created = Counter(database);
        tags.Upsert(1, "vegan");
        tags.UpsertMany(Enumerable.Range(2, 50).Select(key => new LuiEntry<long>(key, "vegan " + key)));
        tags.Remove(2);
        Counter(database).ShouldBe(created, "writing content does not change the schema");

        Execute(database, "CREATE TABLE app(id INTEGER PRIMARY KEY);");
        var table = Counter(database);
        table.ShouldNotBe(created);

        Execute(database, "CREATE INDEX app_id ON app(id);");
        var index = Counter(database);
        index.ShouldNotBe(table);

        Execute(database, "CREATE TRIGGER app_delete AFTER DELETE ON app BEGIN SELECT 1; END;");
        var trigger = Counter(database);
        trigger.ShouldNotBe(index);

        catalog.DropIndex("tags");
        Counter(database).ShouldNotBe(trigger, "a drop of an index of LUI drops its tables");
    }

    [Fact]
    public void The_schema_counter_is_the_same_on_every_connection_of_the_file_and_is_read_on_a_read_only_connection()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        catalog.Create(Tags);

        using var writer = catalog.Database.OpenConnection();
        using var reader = catalog.Database.OpenReadConnection();

        var seen = Database.ReadSchemaCounter(writer);
        Database.ReadSchemaCounter(reader).ShouldBe(seen);
        Counter(database).ShouldBe(seen);
    }

    [Fact]
    public void The_schema_counter_is_read_inside_a_transaction_and_shows_a_change_of_that_transaction()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        catalog.Create(Tags);

        using var connection = database.Connect();
        using var transaction = connection.BeginTransaction();
        var before = Database.ReadSchemaCounter(connection, transaction);

        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "CREATE TABLE app(id INTEGER PRIMARY KEY);";
            command.ExecuteNonQuery();
        }

        Database.ReadSchemaCounter(connection, transaction).ShouldNotBe(before);
        transaction.Rollback();
        Database.ReadSchemaCounter(connection).ShouldBe(before);
    }

    [Fact]
    public void The_schema_counter_is_not_the_format_version_of_the_index()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        catalog.Create(Tags);
        Execute(database, "CREATE TABLE app(id INTEGER PRIMARY KEY);");

        // the LUI format version is a value in a row of lui_meta, the same for every index of the format
        database.Scalar<long>("SELECT schema_version FROM lui_meta WHERE index_name = 'tags'").ShouldBe(2);
        Counter(database).ShouldNotBe(2, "the schema counter counts the changes of the schema of the file");
    }

    private static long Counter(TestDatabase database)
    {
        using var connection = database.Connect();
        return Database.ReadSchemaCounter(connection);
    }

    private static void Execute(TestDatabase database, string sql)
    {
        using var connection = database.Connect();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
