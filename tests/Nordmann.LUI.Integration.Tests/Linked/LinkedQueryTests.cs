using Shouldly;
using static Nordmann.LUI.Integration.Tests.Linked.LinkedTable;

namespace Nordmann.LUI.Integration.Tests.Linked;

/// <summary>
/// What a <c>Linked</c> index answers (ADR-0015): the payload read from columns of the table, the keys, the text read
/// live — and the candidates that the table no longer backs.
/// </summary>
public sealed class LinkedQueryTests
{
    // ---- what comes back -----------------------------------------------------------------------------------------

    [Fact]
    public void The_payload_is_read_from_columns_of_the_table()
    {
        using var database = new TestDatabase();
        Run(database, Table);
        Run(database, "INSERT INTO tag(id, name, usage_count, slug, color) VALUES (1, 'vegan', 5, 'vegan', '#0a0'), (2, 'veganer', 4, 'veganer', NULL);");
        using var catalog = database.Open();
        var tags = catalog.Create("tags", index => index.SingleWord().LinkTo("tag", "id", "name", weight: "usage_count").WithPayload(["slug", "color"], row => new Chip(row.GetString("slug"), row.IsNull("color") ? null : row.GetString("color"))));
        tags.Sync();

        var hits = tags.Search("vegan").Hits;

        hits[0].GetPayload<Chip>().ShouldBe(new Chip("vegan", "#0a0"));
        hits[1].GetPayload<Chip>().ShouldBe(new Chip("veganer"));
    }

    [Fact]
    public void The_payload_row_reads_a_column_in_the_type_the_mapping_asks_for()
    {
        // issue #104 (the coverage of #80, G1): GetInt64, GetDouble and Get<T> were public and called by no test. The
        // values are chosen so that a narrower read would show: a count beyond Int32, a price that a float rounds, a
        // date read as a DateTime, a NULL read as a nullable (#111).
        using var database = new TestDatabase();
        Run(database, """
            CREATE TABLE product (
                id       INTEGER PRIMARY KEY,
                name     TEXT,
                sold     INTEGER,
                price    REAL,
                released TEXT,
                discount INTEGER);
            INSERT INTO product(id, name, sold, price, released, discount)
                VALUES (1, 'apfelkuchen', 5000000000, 3.1, '2026-11-09 08:15:00', NULL),
                       (2, 'apfelstrudel', 7, 2.25, '2025-01-31 00:00:00', 15);
            """);
        using var catalog = database.Open();
        var products = catalog.Create("products", index => index.SingleWord().LinkTo("product", "id", "name")
            .WithPayload(["sold", "price", "released", "discount"], row => new Product(
                row.GetInt64("sold"), row.GetDouble("price"), row.Get<DateTime>("released"), row.Get<long?>("discount"))));
        products.Sync();

        var payloads = products.Search("apfel").Hits
            .Select(hit => hit.GetPayload<Product>()!)
            .OrderBy(product => product.Sold)
            .ToArray();

        payloads.ShouldBe(
        [
            new Product(7, 2.25, new DateTime(2025, 1, 31, 0, 0, 0, DateTimeKind.Unspecified), 15),
            new Product(5_000_000_000, 3.1, new DateTime(2026, 11, 9, 8, 15, 0, DateTimeKind.Unspecified), null),
        ]);
    }

    [Fact]
    public void A_null_column_is_null_for_a_type_that_can_hold_null_and_refused_for_one_that_cannot()
    {
        // issue #111: Get<T> handed every NULL to Microsoft.Data.Sqlite, which refuses it for any T — Get<long?> threw
        // on the first row without a value. A type that can hold null now gets null; a type that cannot still throws,
        // because a missing value read as 0 would be a wrong value without a word (review of 2026-11-04, A.7).
        using var database = new TestDatabase();
        Run(database, """
            CREATE TABLE product (
                id        INTEGER PRIMARY KEY,
                name      TEXT,
                discount  INTEGER,
                note      TEXT,
                withdrawn TEXT);
            INSERT INTO product(id, name, discount, note, withdrawn) VALUES (1, 'apfelkuchen', NULL, NULL, NULL);
            """);
        using var catalog = database.Open();
        var optional = catalog.Create("optional", index => index.SingleWord().LinkTo("product", "id", "name")
            .WithPayload(["discount", "note", "withdrawn"], row => new Optional(
                row.Get<long?>("discount"), row.Get<string>("note"), row.Get<DateTime?>("withdrawn"))));
        optional.Sync();

        optional.Search("apfelkuchen").Hits.ShouldHaveSingleItem().GetPayload<Optional>()
            .ShouldBe(new Optional(null, null, null), "a nullable value type and a reference type read a NULL as null");

        var strict = catalog.Create("strict", index => index.SingleWord().LinkTo("product", "id", "name")
            .WithPayload(["discount"], row => row.Get<long>("discount")));
        strict.Sync();
        Should.Throw<InvalidOperationException>(() => strict.Search("apfelkuchen"), "a long cannot hold a NULL")
            .Message.ShouldContain("NULL");
    }

    [Fact]
    public void A_payload_row_that_is_kept_throws_instead_of_reading_another_hit()
    {
        // Review of 2026-11-04, A.7: the row wraps the reader, which moves on to the next hit. Kept and read later, it used
        // to give the value of whatever the reader stood on — without a word.
        using var database = new TestDatabase();
        Run(database, Table);
        Run(database, "INSERT INTO tag(id, name, usage_count, slug, color) VALUES (1, 'vegan', 5, 'vegan', '#0a0'), (2, 'veganer', 4, 'veganer', NULL);");
        using var catalog = database.Open();
        var kept = new List<LuiPayloadRow>();
        var tags = catalog.Create("tags", index => index.SingleWord().LinkTo("tag", "id", "name", weight: "usage_count").WithPayload(["slug"], row =>
            {
                kept.Add(row);
                return row.GetString("slug");
            }));
        tags.Sync();

        var hits = tags.Search("vegan").Hits;

        hits.Select(hit => hit.GetPayload<string>()).ShouldBe(["vegan", "veganer"], "the row works while the mapping runs");
        kept.Count.ShouldBe(2);
        foreach (var row in kept)
        {
            Should.Throw<InvalidOperationException>(() => row.GetString("slug")).Message.ShouldContain("only while the mapping");
            Should.Throw<InvalidOperationException>(() => row.IsNull("slug"));
        }
    }

    [Fact]
    public void A_payload_column_that_is_not_there_is_refused_at_ensure_and_one_that_was_not_named_at_the_query()
    {
        using var database = new TestDatabase();
        Run(database, Table);
        Insert(database, (1, "vegan", 5));
        using var catalog = database.Open();

        Should.Throw<LuiLinkedTableException>(() => catalog.Create("tags", index => index.SingleWord().LinkTo("tag", "id", "name").WithPayload(["shade"], row => row.GetString("shade"))))
            .Message.ShouldContain("'shade'");

        var tags = catalog.Create("tags", index => index.SingleWord().LinkTo("tag", "id", "name").WithPayload(["slug"], row => row.GetString("color")));
        tags.Sync();

        Should.Throw<ArgumentException>(() => tags.Search("vegan")).Message.ShouldContain("WithPayload");
    }

    [Fact]
    public void A_key_at_the_end_of_the_range_comes_back_as_it_is_and_one_beyond_it_is_left_out_not_refused()
    {
        // review of pull request 14, N6: the key is read back as text; review of pull request 16, M2: a key out of range
        // leaves the row out — one such row must not take the whole index with it
        using var database = new TestDatabase();
        Run(database, Table);
        Insert(database, (4_294_967_295, "vegan", 5), (1_700_000_000_000, "veganer", 5), (-1, "vegane", 5));
        using var catalog = database.Open();
        var tags = Tags(catalog);

        var result = tags.Sync();

        result.Indexed.ShouldBe(1);
        result.KeyOutOfRange.ShouldBe(2);
        tags.Search("vegan").Hits.ShouldHaveSingleItem().Key.ShouldBe(4_294_967_295L);

        // a call of the application with such a key is a mistake of the call, and says so
        Should.Throw<ArgumentOutOfRangeException>(() => tags.Refresh(1_700_000_000_000));
    }

    [Fact]
    public void With_the_triggers_a_row_with_a_key_out_of_range_is_written_and_left_out_of_the_index()
    {
        // review of pull request 16, M2: the trigger must not refuse a write of the application
        using var database = new TestDatabase();
        Run(database, Table);
        using var catalog = database.Open();
        var tags = Tags(catalog);
        tags.CreateTriggers();

        Should.NotThrow(() => Run(database, "INSERT INTO tag(id, name, usage_count) VALUES (4294967296, 'vegan', 5), (1, 'veganer', 3);"));
        tags.ApplyPending().ShouldBe(2);

        database.Scalar<long>("SELECT count(*) FROM tag").ShouldBe(2);
        tags.Pending().ShouldBe(0, "the key out of range leaves the outbox too");
        tags.Search("vegan").Hits.ShouldHaveSingleItem().Key.ShouldBe(1);
    }

    [Fact]
    public void A_multi_word_index_over_a_table_finds_the_words_in_any_order()
    {
        using var database = new TestDatabase();
        Run(database, "CREATE TABLE recipe (id INTEGER PRIMARY KEY, title TEXT NOT NULL, views INTEGER NOT NULL);");
        Run(database, "INSERT INTO recipe VALUES (1, 'Apfel Kuchen', 10), (2, 'Bratapfelkuchen', 1000), (3, 'Gedeckter Apfelkuchen', 500);");
        using var catalog = database.Open();
        var recipes = catalog.Create("recipes", index => index.MultiWord().LinkTo("recipe", "id", "title", weight: "views"));
        recipes.Sync();

        recipes.Search("kuchen apfel").Hits[0].Text.ShouldBe("Apfel Kuchen");
        recipes.Search("apfelkuch").Hits.Select(hit => hit.Key).ShouldContain(1L);
        recipes.Search("ge").Hits.ShouldHaveSingleItem().Key.ShouldBe(3, "the prefix path");
    }

    [Fact]
    public void A_linked_index_answers_as_an_embedded_index_with_the_same_entries()
    {
        using var database = new TestDatabase();
        Run(database, Table);
        var random = new Random(5);
        string[] stems = ["apfel", "kuchen", "vegan", "torte", "birne", "sahne", "zimt", "#äpfel", "straße"];
        var rows = Enumerable.Range(1, 400)
            .Select(i => ((long)i, (string?)$"{stems[random.Next(stems.Length)]}{stems[random.Next(stems.Length)]}{i % 7}", random.Next(0, 1000)))
            .ToArray();
        Insert(database, rows);
        using var catalog = database.Open();
        var linked = Tags(catalog);
        linked.Sync();
        var embedded = catalog.Create("embedded", index => index.SingleWord());
        embedded.UpsertMany(rows.Select(row => new LuiEntry(row.Item1, row.Item2!) { Weight = row.Item3 }));

        foreach (var input in new[] { "a", "ap", "apf", "kuchen", "nka", "veg", "#äpf", "äpfel", "strasse", "torte3", "zimtz" })
        {
            linked.Search(input).Hits.Select(hit => (hit.Key, hit.Text)).ShouldBe(
                embedded.Search(input).Hits.Select(hit => (hit.Key, hit.Text)), $"input '{input}'");
        }
    }

    [Fact]
    public void The_typo_dictionary_of_a_linked_index_learns_from_the_table()
    {
        using var database = new TestDatabase();
        Run(database, Table);
        Insert(database, (1, "Apfelkuchen", 10));
        using var catalog = database.Open();
        var tags = catalog.Create("tags", index => index.SingleWord().TypoTolerance().LinkTo("tag", "id", "name", weight: "usage_count"));
        tags.Sync();

        // the application asks for the build (ADR-0042): a query never does
        tags.WarmUpTypoDictionary();

        tags.Search("apflekuchen").CorrectedQuery.ShouldBe("apfelkuchen");

        Insert(database, (2, "Himbeere", 50));
        tags.Refresh(2);
        tags.Search("himbere").Hits.ShouldHaveSingleItem().Key.ShouldBe(2);
    }

    // ---- the settings of the linked builder (issue #105, the coverage of #80, G2) -----------------------------------

    [Fact]
    public void The_settings_made_after_LinkTo_reach_the_query_and_the_definition()
    {
        // Similarity and GermanTransliteration of LuiLinkedIndexBuilder — the settings made after LinkTo, not before it
        // — were set in no test.
        using var database = new TestDatabase();
        Run(database, Table);
        Insert(database, (1, "Müller", 5));
        using var catalog = database.Open();
        var plain = catalog.Create("plain", index => index.SingleWord().LinkTo("tag", "id", "name")
            .GermanTransliteration(false).Similarity(new Constant(0.25)));
        var german = catalog.Create("german", index => index.SingleWord().LinkTo("tag", "id", "name"));
        plain.Sync();
        german.Sync();

        german.Search("mueller").Hits.ShouldHaveSingleItem().Key.ShouldBe(1, "with the transliteration, ü is ue");
        plain.Search("mueller").Hits.ShouldBeEmpty("without it, ü is not ue");
        plain.Search("mül").Hits.ShouldHaveSingleItem().Similarity.ShouldBe(0.25, "the measure of the builder");
        german.Search("mül").Hits.ShouldHaveSingleItem().Similarity.ShouldNotBe(0.25, "the default measure");

        // the transliteration is part of the definition; the measure is a property of the instance (ADR-0013)
        Should.Throw<LuiDefinitionMismatchException>(() => catalog.Create("plain", index => index.SingleWord().LinkTo("tag", "id", "name")));
        catalog.Create("plain", index => index.SingleWord().LinkTo("tag", "id", "name").GermanTransliteration(false))
            .Search("mül").Hits.ShouldHaveSingleItem().Similarity.ShouldNotBe(0.25, "the measure is not stored");
    }

    [Fact]
    public void A_linked_index_rebuilds_its_typo_dictionary_from_the_table_as_it_is_now()
    {
        // RebuildTypoDictionary of a linked index ran in no test. The dictionary only grows (ADR-0025): a refresh adds
        // the beginnings of new words, it does not move the frequencies. Between two corrections at the same distance
        // the frequency decides, and it follows the weights of the table only when the dictionary is built anew.
        using var database = new TestDatabase();
        Run(database, Table);
        Insert(database, (1, "Torte", 10), (2, "Tarte", 50));
        using var catalog = database.Open();
        var tags = catalog.Create("tags", index => index.SingleWord().TypoTolerance().LinkTo("tag", "id", "name", weight: "usage_count"));
        tags.Sync();
        tags.WarmUpTypoDictionary();
        tags.Search("tirte").CorrectedQuery.ShouldBe("tarte", "the heavier word wins");

        Run(database, "UPDATE tag SET usage_count = 500 WHERE id = 1;");
        tags.Refresh(1).ShouldBeTrue();
        tags.Search("tirte").CorrectedQuery.ShouldBe("tarte", "a refresh does not move the frequencies");

        tags.RebuildTypoDictionary();

        tags.TypoDictionaryState.ShouldBe(LuiTypoDictionaryState.Current);
        tags.Search("tirte").CorrectedQuery.ShouldBe("torte", "the frequencies of the table as it is now");
    }

    [Fact]
    public void The_fluent_query_of_a_linked_index_answers_as_its_search()
    {
        // Query(text) of a linked index ran in no test
        using var database = new TestDatabase();
        Run(database, Table);
        Insert(database, (1, "vegan", 5), (2, "veganer", 9), (3, "vegetarisch", 1));
        using var catalog = database.Open();
        var tags = Tags(catalog);
        tags.Sync();

        tags.Query("veg").Take(2).Execute().Hits.Select(hit => hit.Key)
            .ShouldBe(tags.Search("veg", new LuiQueryOptions { MaxResults = 2 }).Hits.Select(hit => hit.Key));
        tags.Query("veg").Take(2).Execute().Hits.Count.ShouldBe(2);
    }

    // ---- review of pull request 16 ------------------------------------------------------------------------------------

    [Fact]
    public void A_text_that_changed_under_the_index_is_not_a_hit()
    {
        // S1: the candidates come from the index, the text from the table — they must agree
        using var database = new TestDatabase();
        Run(database, Table);
        Insert(database, (1, "vegan", 5), (2, "veganer", 3));
        using var catalog = database.Open();
        var tags = Tags(catalog);
        tags.Sync();

        Run(database, "UPDATE tag SET name = 'zucker' WHERE id = 1;");

        tags.Search("vegan").Hits.ShouldHaveSingleItem().Key.ShouldBe(2, "„zucker\" does not match „vegan\"");
        tags.Search("ve").Hits.ShouldHaveSingleItem().Key.ShouldBe(2, "nor does it begin with „ve\"");
        tags.Search("zuck").Hits.ShouldBeEmpty("the index has not heard of the new text yet");

        tags.Refresh(1);
        tags.Search("zuck").Hits.ShouldHaveSingleItem().Key.ShouldBe(1);
    }

    [Fact]
    public void A_linked_index_reads_the_text_and_the_payload_live_and_keeps_the_weight_until_a_refresh()
    {
        // ADR-0035, spec ADR-0015: LUI keeps no copy of the row — the normalised text in the search index, the weight and the
        // priority in the order of the candidates; the text and the payload of a hit come from the table when it is read
        using var database = new TestDatabase();
        Run(database, Table);
        Insert(database, (1, "vegan", 5), (2, "vegetarisch", 3));
        Run(database, "UPDATE tag SET color = 'green';");
        using var catalog = database.Open();
        var tags = catalog.Create("tags", index => index.SingleWord()
            .LinkTo("tag", "id", "name", weight: "usage_count")
            .WithPayload(["color"], row => row.GetString("color")));
        tags.Sync();

        Run(database, "UPDATE tag SET color = 'teal', usage_count = 900, name = 'veganer' WHERE id = 1; DELETE FROM tag WHERE id = 2;");

        var hit = tags.Search("vegan").Hits.ShouldHaveSingleItem("the deleted row is no hit, before any refresh");
        hit.GetPayload<string>().ShouldBe("teal", "a payload column is read live");
        hit.Text.ShouldBe("veganer", "the text of a row that is still found is read live");
        hit.Weight.ShouldBe(5, "the weight is in the order of the candidates, and that waits for the refresh");
        tags.Search("veganer").Hits.ShouldBeEmpty("the new text is not searchable before the refresh");

        tags.Refresh(1);

        var refreshed = tags.Search("veganer").Hits.ShouldHaveSingleItem();
        refreshed.Weight.ShouldBe(900);
        refreshed.Text.ShouldBe("veganer");
    }

    [Fact]
    public void A_text_that_changed_under_a_multi_word_index_is_not_a_hit()
    {
        using var database = new TestDatabase();
        Run(database, "CREATE TABLE recipe (id INTEGER PRIMARY KEY, title TEXT NOT NULL, views INTEGER NOT NULL);");
        Run(database, "INSERT INTO recipe VALUES (1, 'Apfel Kuchen', 10), (2, 'Kuchen mit Apfel', 5);");
        using var catalog = database.Open();
        var recipes = catalog.Create("recipes", index => index.MultiWord().LinkTo("recipe", "id", "title", weight: "views"));
        recipes.Sync();

        Run(database, "UPDATE recipe SET title = 'Birnen Torte' WHERE id = 1;");

        recipes.Search("kuchen apfel").Hits.ShouldHaveSingleItem().Key.ShouldBe(2);
        recipes.Search("ap").Hits.ShouldBeEmpty();
    }

    // ---- the re-review of pull request 16 ------------------------------------------------------------------------------

    [Fact]
    public void Stale_candidates_do_not_use_up_the_budget_up_to_a_second_budget_of_them()
    {
        // F1 (PR-008): a changed row that is not applied yet is read as a candidate of its old text and then found not to
        // match; it no longer counts against the budget, the read goes on past it — up to another budget of such rows
        using var database = new TestDatabase();
        Run(database, Table);
        Run(database, "WITH RECURSIVE n(i) AS (SELECT 1 UNION ALL SELECT i + 1 FROM n WHERE i < 1200) INSERT INTO tag(id, name, usage_count) SELECT i, 'vegan' || i, i FROM n;");
        using var catalog = database.Open();
        var tags = Tags(catalog);
        tags.Sync();
        tags.CreateTriggers();

        // the 450 most popular change their text: fewer than the budget of 500
        Run(database, "UPDATE tag SET name = 'zucker' || id WHERE id > 750;");
        var found = tags.Search("vegan");
        found.Hits.Count.ShouldBe(20);
        found.Hits.ShouldAllBe(hit => hit.Key.ToInt64() <= 750);
        found.Hits[0].Key.ShouldBe(750);

        // 1,150 among the best: more than a second budget — the answer is empty and says it was cut off
        Run(database, "UPDATE tag SET name = 'zucker' || id WHERE id > 50;");
        var crowded = tags.Search("vegan");
        crowded.Hits.ShouldBeEmpty();
        crowded.Truncated.ShouldBeTrue();
        tags.Search("vegan", new LuiQueryOptions { Candidates = 1_150 }).Hits.Count.ShouldBe(20);

        tags.ApplyPending().ShouldBe(1_150);
        tags.Search("vegan").Hits.Count.ShouldBe(20);
        tags.Search("zucker").Hits.Count.ShouldBe(20);
    }

    [Fact]
    public void An_outdated_row_after_the_budget_does_not_make_the_answer_truncated()
    {
        // review of main after #23, finding 4: the budget was checked before the row was known to be outdated
        using var database = new TestDatabase();
        Run(database, Table);
        Run(database, "WITH RECURSIVE n(i) AS (SELECT 1 UNION ALL SELECT i + 1 FROM n WHERE i < 11) INSERT INTO tag(id, name, usage_count) SELECT i, 'vegan' || i, 100 - i FROM n;");
        using var catalog = database.Open();
        var tags = Tags(catalog);
        tags.Sync();
        Run(database, "UPDATE tag SET name = 'zucker' WHERE id = 11;");

        var result = tags.Search("vegan", new LuiQueryOptions { Candidates = 10 });

        result.Hits.Count.ShouldBe(10);
        result.Truncated.ShouldBeFalse("the row after the budget is outdated: there was nothing more");
    }

    [Fact]
    public void Without_stale_candidates_the_budget_is_what_it_was()
    {
        using var database = new TestDatabase();
        Run(database, Table);
        Run(database, "WITH RECURSIVE n(i) AS (SELECT 1 UNION ALL SELECT i + 1 FROM n WHERE i < 30) INSERT INTO tag(id, name, usage_count) SELECT i, 'vegan' || i, i FROM n;");
        using var catalog = database.Open();
        var tags = Tags(catalog);
        tags.Sync();

        var result = tags.Search("vegan", new LuiQueryOptions { Candidates = 10 });

        result.CandidatesConsidered.ShouldBe(10);
        result.Truncated.ShouldBeTrue();
    }

    /// <summary>The payload of <see cref="The_payload_row_reads_a_column_in_the_type_the_mapping_asks_for"/>.</summary>
    private sealed record Product(long Sold, double Price, DateTime Released, long? Discount);

    /// <summary>The payload of <see cref="A_null_column_is_null_for_a_type_that_can_hold_null_and_refused_for_one_that_cannot"/>.</summary>
    private sealed record Optional(long? Discount, string? Note, DateTime? Withdrawn);

    private sealed class Constant(double value) : ILuiSimilarity
    {
        public double Similarity(string query, string text) => value;
    }
}
