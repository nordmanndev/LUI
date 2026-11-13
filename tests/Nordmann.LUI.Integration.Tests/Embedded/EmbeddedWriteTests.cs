using Shouldly;

namespace Nordmann.LUI.Integration.Tests.Embedded;

/// <summary>
/// The write API of an <c>Embedded</c> single-word index (ADR-0006, ADR-0011, docs/api.md, ADR-0016): the entry, the
/// upsert, the updates, the removal — and a write that changes nothing leaves the search index alone.
/// </summary>
public sealed class EmbeddedWriteTests
{
    [Fact]
    public void Upsert_adds_an_entry_and_replaces_the_one_with_the_same_key()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var tags = catalog.Create("tags", index => index.SingleWord());

        tags.Upsert(7, "apfelkuchen", 10);
        tags.Upsert(7, "birnenkuchen", 20);

        tags.Count().ShouldBe(1);
        tags.Query("apfel").Execute().Hits.ShouldBeEmpty();
        var hit = tags.Query("birnen").Execute().Hits.ShouldHaveSingleItem();
        hit.Key.ShouldBe(7);
        hit.Text.ShouldBe("birnenkuchen");
        hit.Weight.ShouldBe(20);
    }

    [Fact]
    public void The_normalised_forms_are_computed_by_the_database()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var tags = catalog.Create("tags", index => index.SingleWord());

        tags.Upsert(1, "#Äpfel", 1);

        database.Scalar<string>("SELECT s_norm FROM lui_tags").ShouldBe("#aepfel");
        database.Scalar<string>("SELECT s_p1 FROM lui_tags").ShouldBe("a");
        database.Scalar<string>("SELECT s_p2 FROM lui_tags").ShouldBe("ae");
    }

    [Fact]
    public void UpdateWeight_changes_the_weight_and_the_position()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var tags = catalog.Create("tags", index => index.SingleWord());
        tags.Upsert(1, "kuchen a", 10);
        tags.Upsert(2, "kuchen b", 20);
        tags.Query("kuchen").Rank(LuiRankingOrder.WeightOnly).Execute().Hits[0].Key.ShouldBe(2);

        tags.UpdateWeight(1, 500).ShouldBeTrue();

        tags.Query("kuchen").Rank(LuiRankingOrder.WeightOnly).Execute().Hits.Select(h => h.Key).ShouldBe([1, 2]);
        tags.Query("kuchen").Execute().Hits.Single(h => h.Key == 1).Weight.ShouldBe(500);
    }

    [Fact]
    public void UpdatePriority_changes_the_priority_and_the_position()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var tags = catalog.Create("tags", index => index.SingleWord());
        tags.Upsert(1, "kuchen a", 10);
        tags.Upsert(2, "kuchen b", 20);

        tags.UpdatePriority(1, LuiPriority.System).ShouldBeTrue();

        var hits = tags.Query("kuchen").Rank(LuiRankingOrder.WeightOnly).Execute().Hits;
        hits.Select(h => h.Key).ShouldBe([1, 2]);
        hits[0].Priority.ShouldBe(LuiPriority.System);
    }

    [Fact]
    public void Updating_an_entry_that_does_not_exist_says_so()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var tags = catalog.Create("tags", index => index.SingleWord());

        tags.UpdateWeight(99, 1).ShouldBeFalse();
        tags.UpdatePriority(99, LuiPriority.High).ShouldBeFalse();
        tags.Remove(99).ShouldBeFalse();
    }

    [Fact]
    public void Remove_takes_the_entry_out_of_the_search()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var tags = catalog.Create("tags", index => index.SingleWord());
        tags.Upsert(1, "apfelkuchen", 10);

        tags.Remove(1).ShouldBeTrue();

        tags.Count().ShouldBe(0);
        tags.Query("apfelkuchen").Execute().Hits.ShouldBeEmpty();
        tags.Query("ap").Execute().Hits.ShouldBeEmpty();
    }

    [Fact]
    public void The_search_index_stays_consistent_through_every_kind_of_write()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var tags = catalog.Create("tags", index => index.SingleWord());

        for (var i = 1; i <= 50; i++)
        {
            tags.Upsert(i, $"kuchen{i}", i);
        }

        for (var i = 1; i <= 50; i += 2)
        {
            tags.UpdateWeight(i, 1000 - i);
            tags.UpdatePriority(i + 1, LuiPriority.High);
        }

        for (var i = 1; i <= 50; i += 3)
        {
            tags.Remove(i);
        }

        tags.Upsert(2, "torte2", 1);

        // FTS5 compares the index with the content table and throws on any difference
        using var connection = database.Connect();
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO lui_tags_fts(lui_tags_fts, rank) VALUES ('integrity-check', 1);";
        Should.NotThrow(() => command.ExecuteNonQuery());
    }

    [Fact]
    public void A_weight_out_of_range_is_refused_before_it_reaches_sql()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var tags = catalog.Create("tags", index => index.SingleWord());

        Should.Throw<ArgumentOutOfRangeException>(() => tags.Upsert(1, "a", -1));
        Should.Throw<ArgumentOutOfRangeException>(() => tags.Upsert(1, "a", 536_870_912));
        Should.Throw<ArgumentOutOfRangeException>(() => tags.UpdateWeight(1, -1));
        Should.Throw<ArgumentOutOfRangeException>(() => tags.Upsert(1, "a", 1, priority: (LuiPriority)7));
        Should.Throw<ArgumentOutOfRangeException>(() => tags.UpdatePriority(1, (LuiPriority)(-1)));
        tags.Count().ShouldBe(0);
    }

    [Fact]
    public void The_largest_weight_is_accepted()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var tags = catalog.Create("tags", index => index.SingleWord());

        tags.Upsert(1, "vegan", 536_870_911);

        tags.Query("vegan").Execute().Hits.ShouldHaveSingleItem().Weight.ShouldBe(536_870_911);
    }

    [Fact]
    public void An_empty_text_is_refused()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var tags = catalog.Create("tags", index => index.SingleWord());

        Should.Throw<ArgumentException>(() => tags.Upsert(1, string.Empty, 1));
        Should.Throw<ArgumentNullException>(() => tags.Upsert(1, null!, 1));
    }

    [Fact]
    public void A_string_key_works_like_a_long_key()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var tags = catalog.Create("tags", index => index.SingleWord().WithKey(LuiKeyKind.Text));

        tags.Upsert("rezept-42", "apfelkuchen", 10);
        tags.UpdateWeight("rezept-42", 11).ShouldBeTrue();

        tags.Query("apfel").Execute().Hits.ShouldHaveSingleItem().Key.ShouldBe("rezept-42");
        tags.Remove("rezept-42").ShouldBeTrue();
    }

    [Fact]
    public void The_transaction_of_the_application_decides_whether_the_entry_exists()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var tags = catalog.Create("tags", index => index.SingleWord());
        tags.Upsert(1, "apfelkuchen", 10);

        using (var connection = database.Connect())
        {
            using var transaction = connection.BeginTransaction();
            tags.Upsert(2, "birnenkuchen", 20, transaction: transaction);
            tags.UpdateWeight(1, 99, transaction).ShouldBeTrue();
            tags.Remove(1, transaction).ShouldBeTrue();
            transaction.Rollback();
        }

        tags.Count().ShouldBe(1);
        tags.Query("birnen").Execute().Hits.ShouldBeEmpty();
        tags.Query("apfel").Execute().Hits.ShouldHaveSingleItem().Weight.ShouldBe(10);

        using (var connection = database.Connect())
        {
            using var transaction = connection.BeginTransaction();
            tags.Upsert(2, "birnenkuchen", 20, transaction: transaction);
            transaction.Commit();
        }

        tags.Query("birnen").Execute().Hits.ShouldHaveSingleItem().Key.ShouldBe(2);
    }

    // review of pull request 6, B5: Upsert replaces the whole entry
    [Fact]
    public void Upsert_replaces_the_whole_entry_including_the_payload()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var tags = catalog.Create("tags", index => index.SingleWord());
        tags.Upsert(new(1, "vegan") { Weight = 5, Payload = new Chip("vegan") });

        tags.Upsert(1, "vegan", 5);

        tags.Query("vegan").Execute().Hits.ShouldHaveSingleItem().HasPayload.ShouldBeFalse();
    }

    // review of pull request 6, B8: a transaction of another database
    [Fact]
    public void A_transaction_of_another_database_is_refused_with_a_clear_message()
    {
        using var database = new TestDatabase();
        using var other = new TestDatabase();
        using var catalog = database.Open();
        var tags = catalog.Create("tags", index => index.SingleWord());

        using var connection = other.Connect();
        using var transaction = connection.BeginTransaction();

        var exception = Should.Throw<ArgumentException>(() => tags.Upsert(1, "vegan", 5, transaction: transaction));
        exception.Message.ShouldContain("another database");
        exception.ParamName.ShouldBe("transaction");
    }

    // review of pull request 6, doc_id exhaustion
    [Fact]
    public void Running_out_of_document_ids_says_so()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var tags = catalog.Create("tags", index => index.SingleWord());
        using (var connection = database.Connect())
        {
            using var command = connection.CreateCommand();
            command.CommandText =
                "INSERT INTO lui_tags(doc_id, key, text, weight, priority) VALUES (4294967295, 'last', 'last', 1, 3);";
            command.ExecuteNonQuery();
        }

        var exception = Should.Throw<InvalidOperationException>(() => tags.Upsert(2, "one too many", 1));
        exception.Message.ShouldContain("document ids");
    }

    // review of pull request 6, P1: a write that changes nothing leaves the search index alone
    [Fact]
    public void An_identical_upsert_does_not_touch_the_search_index()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var tags = catalog.Create("tags", index => index.SingleWord());
        tags.Upsert(new(1, "vegan") { Weight = 5, Payload = new Chip("v") });
        tags.Upsert(new(2, "vegane torte") { Weight = 4, Payload = new Chip("t") });
        var before = SearchIndexFingerprint(database);

        for (var i = 0; i < 50; i++)
        {
            tags.Upsert(new(1, "vegan") { Weight = 5, Payload = new Chip("v") });
        }

        SearchIndexFingerprint(database).ShouldBe(before);
    }

    // review of pull request 6, P1: a write that changes nothing leaves the search index alone
    [Fact]
    public void A_payload_only_change_does_not_touch_the_search_index_and_the_payload_arrives()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var tags = catalog.Create("tags", index => index.SingleWord());
        tags.Upsert(new(1, "vegan") { Weight = 5, Payload = new Chip("v") });
        var before = SearchIndexFingerprint(database);

        tags.Upsert(new(1, "vegan") { Weight = 5, Payload = new Chip("changed") });
        tags.UpdateWeight(1, 5);
        tags.UpdatePriority(1, LuiPriority.Normal);

        SearchIndexFingerprint(database).ShouldBe(before);
        tags.Query("vegan").Execute().Hits.ShouldHaveSingleItem().GetPayload<Chip>().ShouldBe(new Chip("changed"));
    }

    // review of pull request 6, P1: a write that changes nothing leaves the search index alone
    [Fact]
    public void A_change_that_matters_still_reaches_the_search_index_and_the_index_stays_consistent()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var tags = catalog.Create("tags", index => index.SingleWord());
        tags.Upsert(1, "vegan", 5);
        var before = SearchIndexFingerprint(database);

        tags.Upsert(1, "vegan", 6);
        tags.Upsert(1, "Vegan", 6);
        tags.UpdatePriority(1, LuiPriority.High);
        tags.Upsert(1, "birne", 6, priority: LuiPriority.High);

        string.Equals(SearchIndexFingerprint(database), before, StringComparison.Ordinal).ShouldBeFalse();
        tags.Query("vegan").Execute().Hits.ShouldBeEmpty();
        tags.Query("birne").Execute().Hits.ShouldHaveSingleItem().Key.ShouldBe(1);

        using var connection = database.Connect();
        using var check = connection.CreateCommand();
        check.CommandText = "INSERT INTO lui_tags_fts(lui_tags_fts, rank) VALUES ('integrity-check', 1);";
        Should.NotThrow(() => check.ExecuteNonQuery());
    }

    // the shape of the API before 0.1.0 (ADR-0013), the entry
    [Fact]
    public void An_entry_checks_its_values_where_they_are_set()
    {
        Should.Throw<ArgumentException>(() => new LuiEntry(1, string.Empty))
            .Message.ShouldContain("Text");
        Should.Throw<ArgumentNullException>(() => new LuiEntry(1, null!));
        Should.Throw<ArgumentOutOfRangeException>(() => new LuiEntry(1, "a") { Weight = -1 });
        Should.Throw<ArgumentOutOfRangeException>(() => new LuiEntry(1, "a") { Weight = 536_870_912 });
        Should.Throw<ArgumentOutOfRangeException>(() => new LuiEntry(1, "a") { Priority = (LuiPriority)7 });

        var entry = new LuiEntry(1, "a");
        Should.Throw<ArgumentException>(() => entry with { Text = string.Empty });
    }

    // the shape of the API before 0.1.0 (ADR-0013), the entry
    [Fact]
    public void An_entry_is_normal_and_weightless_unless_it_says_otherwise()
    {
        var entry = new LuiEntry(1, "vegan");

        entry.Weight.ShouldBe(0);
        entry.Priority.ShouldBe(LuiPriority.Normal);
    }

    // the shape of the API before 0.1.0 (ADR-0013), the entry
    [Fact]
    public void The_short_form_of_the_upsert_replaces_the_whole_entry_and_clears_the_payload()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var tags = catalog.Create("tags", index => index.SingleWord());

        tags.Upsert(new LuiEntry(1, "vegan") { Weight = 5, Payload = new Chip("v"), Priority = LuiPriority.System });
        tags.Upsert(1, "vegan", 7);

        var hit = tags.Search("vegan").Hits.ShouldHaveSingleItem();
        hit.HasPayload.ShouldBeFalse();
        hit.Weight.ShouldBe(7);
        hit.Priority.ShouldBe(LuiPriority.Normal);
    }

    private static string SearchIndexFingerprint(TestDatabase database) =>
        database.Scalar<string>(
            "SELECT count(*) || ':' || coalesce(sum(length(block)), 0) || ':' || coalesce(sum(id), 0) FROM lui_tags_fts_data");
}
