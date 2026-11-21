using Microsoft.Data.Sqlite;
using Shouldly;
using static Nordmann.LUI.Integration.Tests.Linked.LinkedTable;

namespace Nordmann.LUI.Integration.Tests.Catalog;

/// <summary>
/// What the guard of a handle remembers, and what it does not (ADR-0075, issue #265, review of pull request 286): a failure is
/// kept and does not reload the definitions at every operation; a transaction of another database is refused before its counter
/// is looked at; a counter that was read inside a transaction of the application — it may be rolled back — is never published to
/// the other handles; and a row of <c>lui_meta</c> that changed with no change of the schema is the one thing the counter does
/// not see.
/// </summary>
public sealed class GuardTests
{
    private static readonly LuiIndexDefinition<long> Tags = new("tags", index => index.SingleWord());

    [Fact]
    public void An_index_of_another_format_is_a_failure_that_is_kept_and_reloads_nothing_again()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var tags = catalog.Create(Tags);
        Run(database, "UPDATE lui_meta SET schema_version = 1 WHERE index_name = 'tags'; CREATE TABLE written_by_another_version(id INTEGER PRIMARY KEY);");

        Should.Throw<LuiDefinitionMismatchException>(() => tags.Count()).Message.ShouldContain("format 1");
        var reloads = catalog.StoredIndexes.Reloads;

        for (var call = 0; call < 5; call++)
        {
            Should.Throw<LuiDefinitionMismatchException>(() => tags.Count()).Message.ShouldContain("format 1");
        }

        catalog.StoredIndexes.Reloads.ShouldBe(reloads, "the failure is kept, the definitions are not read at every operation");
    }

    [Fact]
    public void A_row_that_was_edited_without_a_change_of_the_schema_is_not_seen_and_the_limit_is_pinned()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var tags = catalog.Create(Tags);
        tags.Upsert(1, "vegan");

        // nothing in the schema changes: the counter does not move, and a row of lui_meta that is changed by hand stays invisible —
        // LUI never writes one without its tables, and an application never touches lui_* tables (ADR-0075)
        Run(database, "UPDATE lui_meta SET schema_version = 1 WHERE index_name = 'tags';");

        catalog.Index(new LuiIndexDefinition<long>("tags", index => index.SingleWord())).Count().ShouldBe(1);
        tags.Count().ShouldBe(1);
    }

    [Fact]
    public void A_transaction_of_another_database_is_refused_before_the_schema_counter_is_looked_at()
    {
        using var database = new TestDatabase();
        using var other = new TestDatabase();
        other.PrepareWithATable();
        using var catalog = database.Open();
        var tags = catalog.Create(Tags);
        _ = tags.Count();
        var reloads = catalog.StoredIndexes.Reloads;

        using var connection = other.Connect();
        using var transaction = connection.BeginTransaction();

        Should.Throw<ArgumentException>(() => tags.Upsert(1, "vegan", transaction: transaction)).ParamName.ShouldBe("transaction");
        catalog.StoredIndexes.Reloads.ShouldBe(reloads, "the counter of another file is not the counter of this one");
        tags.Count().ShouldBe(0);
        catalog.StoredIndexes.Reloads.ShouldBe(reloads, "and it was not remembered");
    }

    [Fact]
    public void A_counter_validated_in_one_transaction_of_the_application_is_not_trusted_in_the_next()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var tags = catalog.Create(Tags);
        _ = tags.Count();

        using var connection = database.Connect();

        // the first transaction changes the schema, writes through the handle — validated at its own counter — and is rolled back
        using (var first = connection.BeginTransaction())
        {
            Execute(connection, first, "CREATE TABLE migrating(id INTEGER PRIMARY KEY);");
            tags.Upsert(1, "vegan", transaction: first);
            first.Rollback();
        }

        // the second one reaches the same counter by one change of the schema — and the definition of the index is another one now
        using var second = connection.BeginTransaction();
        Execute(connection, second, "UPDATE lui_meta SET definition = replace(definition, 'SingleWord', 'MultiWord') WHERE index_name = 'tags'; CREATE TABLE migrating_too(id INTEGER PRIMARY KEY);");

        Should.Throw<LuiDefinitionMismatchException>(() => tags.Upsert(2, "vegetarisch", transaction: second));
        second.Rollback();
    }

    [Fact]
    public void A_read_that_pinned_its_snapshot_between_a_drop_and_a_create_ends_in_the_proper_exception()
    {
        using var database = new TestDatabase();
        using var first = database.Open();
        using var second = database.Open();
        first.Create(Tags).Upsert(1, "vegan");
        var tags = second.Index(Tags);
        _ = tags.Count();
        first.DropIndex("tags");

        // the snapshot of the read is pinned by the check, with the index gone; the index is created again, with the same
        // definition, a moment later — the definitions that are read now are not the ones of the snapshot
        second.Database.AfterSchemaCheck = () =>
        {
            second.Database.AfterSchemaCheck = null;
            first.Create(Tags);
        };

        var failure = Record.Exception(() => tags.Count());

        failure.ShouldBeOfType<LuiIndexNotFoundException>("the read sees what its snapshot sees, never a bare 'no such table'");
    }

    private static void Execute(SqliteConnection connection, SqliteTransaction transaction, string sql)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    [Fact]
    public void A_counter_read_inside_a_transaction_of_the_application_is_not_published_to_the_other_handles()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var tags = catalog.Create(Tags);
        _ = tags.Count();

        using var connection = database.Connect();
        using var transaction = connection.BeginTransaction();
        using (var create = connection.CreateCommand())
        {
            create.Transaction = transaction;
            create.CommandText = "CREATE TABLE migrating(id INTEGER PRIMARY KEY);";
            create.ExecuteNonQuery();
        }

        tags.Upsert(1, "vegan", transaction: transaction);
        var reloads = catalog.StoredIndexes.Reloads;
        transaction.Rollback();

        // the counter of the transaction is gone with it: the value the handle validated at is the one of the file
        tags.Count().ShouldBe(0);
        catalog.StoredIndexes.Reloads.ShouldBe(reloads, "an uncommitted counter was not published, so the committed one is still the validated one");
    }
}
