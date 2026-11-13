using Shouldly;
using static Nordmann.LUI.Integration.Tests.Linked.LinkedTable;

namespace Nordmann.LUI.Integration.Tests.Linked;

/// <summary>
/// The application keeps a <c>Linked</c> index current (ADR-0016, ADR-0015): the sync of the whole table,
/// the refresh of single rows, in a transaction of their own or in the one of the application.
/// </summary>
public sealed class LinkedSyncTests
{
    // ---- the application keeps the index current ------------------------------------------------------------------

    [Fact]
    public void An_index_starts_empty_and_a_sync_indexes_every_row_with_a_text()
    {
        using var database = new TestDatabase();
        Run(database, Table);
        Insert(database, (1, "vegan", 5), (2, "vegetarisch", 9), (3, null, 7), (4, string.Empty, 7));
        using var catalog = database.Open();
        var tags = Tags(catalog);

        tags.Count().ShouldBe(0);
        tags.Search("veg").Hits.ShouldBeEmpty();

        var result = tags.Sync();

        result.Indexed.ShouldBe(2);
        result.WithoutText.ShouldBe(2);
        result.KeyOutOfRange.ShouldBe(0);
        tags.Count().ShouldBe(2);
        // word start, then similarity, then weight (ADR-0013, ADR-0055): „vegan" is nearer to „veg" than „vegetarisch"
        tags.Search("veg").Hits.Select(hit => (hit.Key, hit.Text, hit.Weight)).ShouldBe([(1L, "vegan", 5), (2L, "vegetarisch", 9)]);
    }

    [Fact]
    public void A_sync_takes_in_every_change_the_index_did_not_hear_of()
    {
        using var database = new TestDatabase();
        Run(database, Table);
        Insert(database, (1, "vegan", 5), (2, "kuchen", 9));
        using var catalog = database.Open();
        var tags = Tags(catalog);
        tags.Sync();

        Run(database, "UPDATE tag SET name = 'torte' WHERE id = 2; DELETE FROM tag WHERE id = 1; INSERT INTO tag(id, name) VALUES (3, 'tofu');");
        // the index did not hear of the change: the old text is no hit any more (S1), the new one not yet
        tags.Search("kuchen").Hits.ShouldBeEmpty();
        tags.Search("torte").Hits.ShouldBeEmpty();
        tags.Count().ShouldBe(2);

        tags.Sync().Indexed.ShouldBe(2);

        tags.Search("kuchen").Hits.ShouldBeEmpty();
        tags.Search("vegan").Hits.ShouldBeEmpty();
        tags.Search("torte").Hits.ShouldHaveSingleItem().Key.ShouldBe(2);
        tags.Search("tofu").Hits.ShouldHaveSingleItem().Key.ShouldBe(3);
    }

    [Fact]
    public void A_refresh_without_a_transaction_follows_an_insert_an_update_and_a_delete()
    {
        using var database = new TestDatabase();
        Run(database, Table);
        using var catalog = database.Open();
        var tags = Tags(catalog);

        Insert(database, (7, "apfelkuchen", 10));
        tags.Refresh(7).ShouldBeTrue();
        tags.Search("apfelk").Hits.ShouldHaveSingleItem().Key.ShouldBe(7);

        Run(database, "UPDATE tag SET name = 'birnenkuchen', usage_count = 20 WHERE id = 7;");
        tags.Refresh(7).ShouldBeTrue();
        tags.Search("apfelk").Hits.ShouldBeEmpty();
        tags.Search("birnen").Hits.ShouldHaveSingleItem().Weight.ShouldBe(20);

        Run(database, "DELETE FROM tag WHERE id = 7;");
        tags.Refresh(7).ShouldBeFalse();
        tags.Search("birnen").Hits.ShouldBeEmpty();
        tags.Count().ShouldBe(0);
    }

    [Fact]
    public void A_refresh_in_the_transaction_of_the_application_is_part_of_it()
    {
        using var database = new TestDatabase();
        Run(database, Table);
        using var catalog = database.Open();
        var tags = Tags(catalog);

        using (var connection = database.Connect())
        using (var transaction = connection.BeginTransaction())
        {
            Run(connection, transaction, "INSERT INTO tag(id, name, usage_count) VALUES (1, 'vegan', 5);");
            tags.Refresh(1, transaction).ShouldBeTrue();
            transaction.Rollback();
        }

        tags.Count().ShouldBe(0);
        tags.Search("vegan").Hits.ShouldBeEmpty();

        using (var connection = database.Connect())
        using (var transaction = connection.BeginTransaction())
        {
            Run(connection, transaction, "INSERT INTO tag(id, name, usage_count) VALUES (1, 'vegan', 5);");
            tags.Refresh(1, transaction);
            transaction.Commit();
        }

        tags.Search("vegan").Hits.ShouldHaveSingleItem().Key.ShouldBe(1);
    }

    [Fact]
    public void Refresh_many_counts_the_rows_that_are_in_the_index_and_a_cancellation_rolls_back()
    {
        using var database = new TestDatabase();
        Run(database, Table);
        Insert(database, (1, "apfel", 1), (2, "birne", 2), (3, "kirsche", 3));
        using var catalog = database.Open();
        var tags = Tags(catalog);

        tags.RefreshMany([1, 2, 3, 99]).ShouldBe(3);
        tags.Count().ShouldBe(3);

        Run(database, "DELETE FROM tag;");
        using var cancellation = new CancellationTokenSource();
        IEnumerable<long> Keys()
        {
            yield return 1;
            cancellation.Cancel();
            yield return 2;
        }

        Should.Throw<OperationCanceledException>(() => tags.RefreshMany(Keys(), cancellationToken: cancellation.Token));
        tags.Count().ShouldBe(3, "LUI's own transaction is rolled back");
    }

    [Fact]
    public void The_weight_and_the_priority_come_from_their_columns_and_are_brought_into_range()
    {
        using var database = new TestDatabase();
        Run(database, Table);
        Run(database, """
            INSERT INTO tag(id, name, usage_count, priority) VALUES
                (1, 'vegan',       5,         0),
                (2, 'veganer',     900,       NULL),
                (3, 'vegane',      -40,       9),
                (4, 'veganismus',  999999999, 3);
            """);
        using var catalog = database.Open();
        var tags = catalog.CreateLinked("tags", index => index.SingleWord().LinkTo("tag", "id", "name", weight: "usage_count", priority: "priority"));
        tags.Sync();

        var hits = tags.Search("vegan").Hits;

        hits[0].Key.ShouldBe(1, "the system priority comes first");
        hits[0].Priority.ShouldBe(LuiPriority.System);
        hits.Single(hit => hit.Key == 3).Weight.ShouldBe(0);
        hits.Single(hit => hit.Key == 3).Priority.ShouldBe(LuiPriority.Normal);
        hits.Single(hit => hit.Key == 4).Weight.ShouldBe(536_870_911);
    }

    // ---- review of pull request 16 ------------------------------------------------------------------------------------

    [Fact]
    public void A_key_that_comes_twice_is_refreshed_once_and_counted_once()
    {
        // N2
        using var database = new TestDatabase();
        Run(database, Table);
        Insert(database, (1, "vegan", 5));
        using var catalog = database.Open();
        var tags = Tags(catalog);

        tags.RefreshMany([1, 1, 1]).ShouldBe(1);
        tags.Count().ShouldBe(1);
    }

    [Fact]
    public void A_transaction_of_another_database_is_refused_with_a_clear_message()
    {
        // N5
        using var database = new TestDatabase();
        using var other = new TestDatabase();
        Run(database, Table);
        Run(other, Table);
        using var catalog = database.Open();
        var tags = Tags(catalog);

        using var connection = other.Connect();
        using var transaction = connection.BeginTransaction();

        Should.Throw<ArgumentException>(() => tags.Refresh(1, transaction)).Message.ShouldContain("another database");
    }
}
