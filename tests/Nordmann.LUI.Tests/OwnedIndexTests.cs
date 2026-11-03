using Microsoft.Data.Sqlite;
using Shouldly;

namespace Nordmann.LUI.Tests;

/// <summary>
/// The schema and the write API of an <c>Owned</c> single-word index
/// (LUI-Spezifikation §7.2, §7.4, §12.2, §12.4, §12.8).
/// </summary>
public sealed class OwnedIndexTests
{
    [Fact]
    public void Ensure_creates_the_tables_the_indexes_the_search_index_and_the_triggers()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();

        catalog.Index("recipes").SingleWord().Owned<long>().Ensure();

        var tables = Names(database, "type = 'table'");
        tables.Contains("lui_recipes", StringComparer.Ordinal).ShouldBeTrue();
        tables.Contains("lui_recipes_fts", StringComparer.Ordinal).ShouldBeTrue();
        Names(database, "type = 'index' AND tbl_name = 'lui_recipes' AND name NOT LIKE 'sqlite_%'")
            .ShouldBe(["lui_recipes_p1", "lui_recipes_p2", "lui_recipes_rowid"]);
        Names(database, "type = 'trigger'").ShouldBe(["lui_recipes_ad", "lui_recipes_ai", "lui_recipes_au"]);
    }

    [Fact]
    public void Ensure_writes_the_definition_into_the_meta_table()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();

        catalog.Index("recipes").SingleWord().Owned<long>().Ensure();

        database.Scalar<string>("SELECT kind FROM lui_meta WHERE index_name = 'recipes'").ShouldBe("SingleWord");
        database.Scalar<string>("SELECT source FROM lui_meta WHERE index_name = 'recipes'").ShouldBe("Owned");
        database.Scalar<string>("SELECT definition FROM lui_meta WHERE index_name = 'recipes'")
            .ShouldContain("\"germanTransliteration\":true");
    }

    [Fact]
    public void Ensure_is_idempotent_and_keeps_the_entries()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var first = catalog.Index("tags").SingleWord().Owned<long>().Ensure();
        first.Upsert(1, "vegan", 5);

        var second = catalog.Index("tags").SingleWord().Owned<long>().Ensure();

        second.Count().ShouldBe(1);
        database.Scalar<long>("SELECT count(*) FROM lui_meta").ShouldBe(1);
    }

    [Fact]
    public void Ensure_with_another_definition_fails_and_names_both()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        catalog.Index("tags").SingleWord().Owned<long>().Ensure();

        var exception = Should.Throw<LuiDefinitionMismatchException>(
            () => catalog.Index("tags").SingleWord().Owned<long>().GermanTransliteration(false).Ensure());

        exception.IndexName.ShouldBe("tags");
        exception.Stored.ShouldContain("\"germanTransliteration\":true");
        exception.Requested.ShouldContain("\"germanTransliteration\":false");
        exception.Message.ShouldContain("stored:");
        exception.Message.ShouldContain("requested:");
    }

    [Fact]
    public void A_payload_changes_the_definition()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        catalog.Index("tags").SingleWord().Owned<long>().Ensure();

        Should.Throw<LuiDefinitionMismatchException>(
            () => catalog.Index("tags").SingleWord().Owned<long>().WithPayload(ChipJsonContext.Default.Chip).Ensure());
    }

    [Fact]
    public void A_key_of_another_type_changes_the_definition()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        catalog.Index("tags").SingleWord().Owned<long>().Ensure();

        Should.Throw<LuiDefinitionMismatchException>(
            () => catalog.Index("tags").SingleWord().Owned<string>().Ensure());
    }

    [Fact]
    public void An_index_stored_with_another_fold_table_is_refused()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        catalog.Index("tags").SingleWord().Owned<long>().Ensure();

        using (var connection = database.Connect())
        {
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE lui_meta SET definition = replace(definition, '\"foldTable\":\"', '\"foldTable\":\"old-')";
            command.ExecuteNonQuery();
        }

        var exception = Should.Throw<LuiDefinitionMismatchException>(
            () => catalog.Index("tags").SingleWord().Owned<long>().Ensure());
        exception.Stored.ShouldContain("old-");
    }

    [Fact]
    public void Drop_removes_everything_and_the_way_out_of_a_mismatch()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        catalog.Index("tags").SingleWord().Owned<long>().Ensure().Upsert(1, "vegan", 5);

        catalog.DropIndex("tags").ShouldBeTrue();

        database.Scalar<long>("SELECT count(*) FROM sqlite_master WHERE name LIKE 'lui_tags%'").ShouldBe(0);
        database.Scalar<long>("SELECT count(*) FROM lui_meta").ShouldBe(0);
        catalog.DropIndex("tags").ShouldBeFalse();

        // and the index can be ensured again, with another definition
        Should.NotThrow(() => catalog.Index("tags").SingleWord().Owned<long>().GermanTransliteration(false).Ensure());
    }

    [Fact]
    public void Indexes_are_independent_of_each_other()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var tags = catalog.Index("tags").SingleWord().Owned<long>().Ensure();
        var words = catalog.Index("words").SingleWord().Owned<long>().Ensure();

        tags.Upsert(1, "vegan", 5);
        words.Upsert(1, "apfelkuchen", 5);

        tags.Query("vegan").Execute().Hits.Count.ShouldBe(1);
        tags.Query("apfel").Execute().Hits.ShouldBeEmpty();
        words.Query("apfel").Execute().Hits.Count.ShouldBe(1);
    }

    [Fact]
    public void Upsert_adds_an_entry_and_replaces_the_one_with_the_same_key()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var index = catalog.Index("tags").SingleWord().Owned<long>().Ensure();

        index.Upsert(7, "apfelkuchen", 10);
        index.Upsert(7, "birnenkuchen", 20);

        index.Count().ShouldBe(1);
        index.Query("apfel").Execute().Hits.ShouldBeEmpty();
        var hit = index.Query("birnen").Execute().Hits.ShouldHaveSingleItem();
        hit.Key.ShouldBe(7);
        hit.Text.ShouldBe("birnenkuchen");
        hit.Weight.ShouldBe(20);
    }

    [Fact]
    public void The_normalised_forms_are_computed_by_the_database()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var index = catalog.Index("tags").SingleWord().Owned<long>().Ensure();

        index.Upsert(1, "#Äpfel", 1);

        database.Scalar<string>("SELECT s_norm FROM lui_tags").ShouldBe("#aepfel");
        database.Scalar<string>("SELECT s_p1 FROM lui_tags").ShouldBe("a");
        database.Scalar<string>("SELECT s_p2 FROM lui_tags").ShouldBe("ae");
    }

    [Fact]
    public void UpdateWeight_changes_the_weight_and_the_position()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var index = catalog.Index("tags").SingleWord().Owned<long>().Ensure();
        index.Upsert(1, "kuchen a", 10);
        index.Upsert(2, "kuchen b", 20);
        index.Query("kuchen").Rank(LuiRankingOrder.WeightOnly).Execute().Hits[0].Key.ShouldBe(2);

        index.UpdateWeight(1, 500).ShouldBeTrue();

        index.Query("kuchen").Rank(LuiRankingOrder.WeightOnly).Execute().Hits.Select(h => h.Key).ShouldBe([1, 2]);
        index.Query("kuchen").Execute().Hits.Single(h => h.Key == 1).Weight.ShouldBe(500);
    }

    [Fact]
    public void UpdatePriority_changes_the_priority_and_the_position()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var index = catalog.Index("tags").SingleWord().Owned<long>().Ensure();
        index.Upsert(1, "kuchen a", 10);
        index.Upsert(2, "kuchen b", 20);

        index.UpdatePriority(1, LuiPriority.System).ShouldBeTrue();

        var hits = index.Query("kuchen").Rank(LuiRankingOrder.WeightOnly).Execute().Hits;
        hits.Select(h => h.Key).ShouldBe([1, 2]);
        hits[0].Priority.ShouldBe(LuiPriority.System);
    }

    [Fact]
    public void Updating_an_entry_that_does_not_exist_says_so()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var index = catalog.Index("tags").SingleWord().Owned<long>().Ensure();

        index.UpdateWeight(99, 1).ShouldBeFalse();
        index.UpdatePriority(99, LuiPriority.High).ShouldBeFalse();
        index.Remove(99).ShouldBeFalse();
    }

    [Fact]
    public void Remove_takes_the_entry_out_of_the_search()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var index = catalog.Index("tags").SingleWord().Owned<long>().Ensure();
        index.Upsert(1, "apfelkuchen", 10);

        index.Remove(1).ShouldBeTrue();

        index.Count().ShouldBe(0);
        index.Query("apfelkuchen").Execute().Hits.ShouldBeEmpty();
        index.Query("ap").Execute().Hits.ShouldBeEmpty();
    }

    [Fact]
    public void The_search_index_stays_consistent_through_every_kind_of_write()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var index = catalog.Index("tags").SingleWord().Owned<long>().Ensure();

        for (var i = 1; i <= 50; i++)
        {
            index.Upsert(i, $"kuchen{i}", i);
        }

        for (var i = 1; i <= 50; i += 2)
        {
            index.UpdateWeight(i, 1000 - i);
            index.UpdatePriority(i + 1, LuiPriority.High);
        }

        for (var i = 1; i <= 50; i += 3)
        {
            index.Remove(i);
        }

        index.Upsert(2, "torte2", 1);

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
        var index = catalog.Index("tags").SingleWord().Owned<long>().Ensure();

        Should.Throw<ArgumentOutOfRangeException>(() => index.Upsert(1, "a", -1));
        Should.Throw<ArgumentOutOfRangeException>(() => index.Upsert(1, "a", 536_870_912));
        Should.Throw<ArgumentOutOfRangeException>(() => index.UpdateWeight(1, -1));
        Should.Throw<ArgumentOutOfRangeException>(() => index.Upsert(1, "a", 1, priority: (LuiPriority)7));
        Should.Throw<ArgumentOutOfRangeException>(() => index.UpdatePriority(1, (LuiPriority)(-1)));
        index.Count().ShouldBe(0);
    }

    [Fact]
    public void The_largest_weight_is_accepted()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var index = catalog.Index("tags").SingleWord().Owned<long>().Ensure();

        index.Upsert(1, "vegan", 536_870_911);

        index.Query("vegan").Execute().Hits.ShouldHaveSingleItem().Weight.ShouldBe(536_870_911);
    }

    [Fact]
    public void An_empty_text_is_refused()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var index = catalog.Index("tags").SingleWord().Owned<long>().Ensure();

        Should.Throw<ArgumentException>(() => index.Upsert(1, string.Empty, 1));
        Should.Throw<ArgumentNullException>(() => index.Upsert(1, null!, 1));
    }

    [Fact]
    public void A_string_key_works_like_a_long_key()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var index = catalog.Index("tags").SingleWord().Owned<string>().Ensure();

        index.Upsert("rezept-42", "apfelkuchen", 10);
        index.UpdateWeight("rezept-42", 11).ShouldBeTrue();

        index.Query("apfel").Execute().Hits.ShouldHaveSingleItem().Key.ShouldBe("rezept-42");
        index.Remove("rezept-42").ShouldBeTrue();
    }

    [Fact]
    public void A_payload_comes_back_with_the_hit()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var index = catalog.Index("tags").SingleWord().Owned<long>().WithPayload(ChipJsonContext.Default.Chip).Ensure();

        index.Upsert(new(1, "vegan") { Weight = 5, Payload = new Chip("vegan", "#0a0") });
        index.Upsert(2, "veganer-kuchen", 4);

        var hits = index.Query("vegan").Rank(LuiRankingOrder.WeightOnly).Execute().Hits;
        hits[0].Payload.ShouldBe(new Chip("vegan", "#0a0"));
        hits[1].Payload.ShouldBeNull();
    }

    [Fact]
    public void A_payload_is_read_for_the_hits_only()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var serializer = new CountingSerializer();
        var index = catalog.Index("tags").SingleWord().Owned<long>().WithPayload(serializer).Ensure();
        for (var i = 1; i <= 200; i++)
        {
            index.Upsert(new(i, $"kuchen{i:000}") { Weight = i, Payload = new Chip($"k{i}") });
        }

        var result = index.Query("kuchen").Take(5).Execute();

        result.CandidatesConsidered.ShouldBe(200);
        result.Hits.Count.ShouldBe(5);
        serializer.Deserialized.ShouldBe(5);
    }

    [Fact]
    public void The_transaction_of_the_application_decides_whether_the_entry_exists()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var index = catalog.Index("tags").SingleWord().Owned<long>().Ensure();
        index.Upsert(1, "apfelkuchen", 10);

        using (var connection = database.Connect())
        {
            using var transaction = connection.BeginTransaction();
            index.Upsert(2, "birnenkuchen", 20, transaction: transaction);
            index.UpdateWeight(1, 99, transaction).ShouldBeTrue();
            index.Remove(1, transaction).ShouldBeTrue();
            transaction.Rollback();
        }

        index.Count().ShouldBe(1);
        index.Query("birnen").Execute().Hits.ShouldBeEmpty();
        index.Query("apfel").Execute().Hits.ShouldHaveSingleItem().Weight.ShouldBe(10);

        using (var connection = database.Connect())
        {
            using var transaction = connection.BeginTransaction();
            index.Upsert(2, "birnenkuchen", 20, transaction: transaction);
            transaction.Commit();
        }

        index.Query("birnen").Execute().Hits.ShouldHaveSingleItem().Key.ShouldBe(2);
    }

    [Fact]
    public void The_entries_survive_a_new_catalog_on_the_same_file()
    {
        using var database = new TestDatabase();
        using (var catalog = database.Open())
        {
            catalog.Index("tags").SingleWord().Owned<long>().Ensure().Upsert(1, "vegan", 5);
        }

        using var again = database.Open();
        var index = again.Index("tags").SingleWord().Owned<long>().Ensure();

        index.Query("vegan").Execute().Hits.ShouldHaveSingleItem().Key.ShouldBe(1);
    }

    private static List<string> Names(TestDatabase database, string condition)
    {
        using var connection = database.Connect();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT name FROM sqlite_master WHERE {condition} ORDER BY name;";

        var names = new List<string>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }

    private sealed class CountingSerializer : ILuiPayloadSerializer<Chip>
    {
        internal int Deserialized { get; private set; }

        public byte[] Serialize(Chip payload) => System.Text.Encoding.UTF8.GetBytes(payload.Slug);

        public Chip Deserialize(ReadOnlySpan<byte> bytes)
        {
            Deserialized++;
            return new Chip(System.Text.Encoding.UTF8.GetString(bytes));
        }
    }
}
