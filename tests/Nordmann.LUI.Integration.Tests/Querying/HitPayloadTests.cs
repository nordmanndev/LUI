#pragma warning disable MA0048 // the payload of these tests and the JSON context that knows it: one place
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using Nordmann.LUI.Integration.Tests.Embedded;
using Shouldly;
using static Nordmann.LUI.Integration.Tests.Linked.LinkedTable;

namespace Nordmann.LUI.Integration.Tests.Querying;

/// <summary>A payload of the shape of a search suggestion: five fields, about 200 bytes of JSON.</summary>
/// <param name="Title">The title.</param>
/// <param name="Author">The author.</param>
/// <param name="Minutes">The minutes.</param>
/// <param name="Rating">The rating.</param>
/// <param name="Category">The category.</param>
public sealed record SuggestionPayload(string Title, string Author, int Minutes, double Rating, string Category);

/// <summary>The JSON context of <see cref="SuggestionPayload"/>.</summary>
[JsonSerializable(typeof(SuggestionPayload))]
public sealed partial class SuggestionJsonContext : JsonSerializerContext;

/// <summary>
/// The payload of a hit (ADR-0074, issue #262): a query on an index with payload loads the payloads of its hits with the query,
/// and a query on an index without one reads none.
/// </summary>
public sealed class HitPayloadTests
{
    private const string Rows = "INSERT INTO tag(id, name, usage_count, slug, color) VALUES (1, 'vegan', 5, 'vegan', '#0a0'), (2, 'veganer', 4, 'veganer', NULL);";

    private static readonly LuiIndexDefinition<long, Chip> Chips = new("chips", index => index.SingleWord());

    private static readonly LuiIndexDefinition<string, Chip> NamedChips = new("named_chips", index => index.SingleWord());

    private static readonly LuiIndexDefinition<long> Plain = new("plain", index => index.SingleWord());

    private static readonly LuiIndexDefinition<long> Bare = new("bare", index => index.SingleWord());

    /// <summary>The registry of these tests: the chips in the JSON of LUI.</summary>
    private static LuiCatalogOptions Json() => new() { Payloads = new LuiPayloadRegistry().Use(ChipJsonContext.Default) };

    private static void Execute(TestDatabase database, string sql)
    {
        using var connection = database.Connect();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static TestDatabase Prepared()
    {
        var database = new TestDatabase();
        Run(database, Table);
        Run(database, Rows);
        return database;
    }

    [Fact]
    public void The_payload_of_an_embedded_hit_is_set_when_the_query_returns_and_reading_it_calls_nothing()
    {
        using var database = new TestDatabase();
        var serializer = new LinesChipSerializer();
        using var catalog = database.Open(new LuiCatalogOptions { Payloads = new LuiPayloadRegistry().Use(serializer) });
        var chips = catalog.Create(Chips);
        chips.UpsertMany(
        [
            new LuiEntry<long, Chip>(1, "vegan", new Chip("one", "green")) { Weight = 9 },
            new LuiEntry<long, Chip>(2, "veganer", new Chip("two")) { Weight = 8 },
            new LuiEntry<long, Chip>(3, "vegetarisch", new Chip("three")) { Weight = 7 },
            new LuiEntry<long, Chip>(4, "tofu", new Chip("four")),
        ]);
        serializer.Deserialized.ShouldBe(0, "a write reads no payload");

        var result = chips.Search("veg");

        result.Hits.Select(hit => hit.Key).ShouldBe([1L, 2L, 3L]);
        serializer.Deserialized.ShouldBe(3, "one call per hit, during the query");
        result.Hits[0].Payload.ShouldBe(new Chip("one", "green"));
        result.Hits[1].Payload.ShouldBe(new Chip("two"));
        result.Hits[2].Payload.ShouldBe(new Chip("three"));
        foreach (var hit in result.Hits)
        {
            hit.Payload.ShouldBeSameAs(hit.Payload, "a plain value: the same object on every read");
        }

        serializer.Deserialized.ShouldBe(3, "reading hit.Payload calls no serializer");

        chips.Query("tofu").Execute().Hits.ShouldHaveSingleItem().Payload.ShouldBe(new Chip("four"));
        serializer.Deserialized.ShouldBe(4);
    }

    [Fact]
    public void The_payload_of_a_linked_hit_is_built_when_the_query_returns_and_reading_it_calls_nothing()
    {
        using var database = Prepared();
        using var catalog = database.Open();
        var calls = 0;
        var chips = catalog.Create(
            new LuiLinkedIndexDefinition<long, Chip>(
                "chips",
                index => index.SingleWord().LinkTo("tag", "id", "name", weight: "usage_count")
                    .WithPayload(["slug", "color"], row =>
                    {
                        Interlocked.Increment(ref calls);
                        return new Chip(row.GetString("slug"), row.IsNull("color") ? null : row.GetString("color"));
                    })));
        chips.Sync();
        calls = 0;

        var result = chips.Search("vegan");

        result.Hits.Select(hit => hit.Key).ShouldBe([1L, 2L]);
        calls.ShouldBe(2, "the map ran once per hit, during the query");
        result.Hits[0].Payload.ShouldBe(new Chip("vegan", "#0a0"));
        result.Hits[1].Payload.ShouldBe(new Chip("veganer"));
        calls.ShouldBe(2, "reading hit.Payload calls no map");
    }

    [Fact]
    public void A_hit_equals_another_with_the_same_values_and_the_same_payload()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open(Json());
        var chips = catalog.Create(Chips);
        chips.Upsert(1, "vegan", new Chip("one"));

        var first = chips.Search("vegan").Hits.ShouldHaveSingleItem();
        var second = chips.Search("vegan").Hits.ShouldHaveSingleItem();

        first.ShouldNotBeSameAs(second);
        first.ShouldBe(second);
        (first with { Payload = new Chip("other") }).ShouldNotBe(second);
    }

    [Fact]
    public void An_unreadable_payload_fails_the_query_naming_index_key_and_type()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open(Json());
        var chips = catalog.Create(Chips);
        chips.UpsertMany([new LuiEntry<long, Chip>(1, "vegan", new Chip("one")), new LuiEntry<long, Chip>(2, "veganer", new Chip("two"))]);

        // the strict JSON of LUI refuses what is not a chip: a payload of an old shape
        Execute(database, "UPDATE lui_chips SET payload = X'7B226F6C64223A317D' WHERE key = '2';");

        var exception = Should.Throw<LuiPayloadException>(() => chips.Search("vegan"));

        exception.IndexName.ShouldBe("chips");
        exception.Key.ShouldBe("2");
        exception.RequestedType.ShouldBe(typeof(Chip));
        exception.InnerException.ShouldNotBeNull();
        exception.Message.ShouldContain("'chips'", Case.Sensitive);
        exception.Message.ShouldContain("'2'", Case.Sensitive);
        exception.Message.ShouldContain(typeof(Chip).FullName!, Case.Sensitive);

        // the fluent path is the same query, and a query that does not reach the entry succeeds: nothing is partial
        Should.Throw<LuiPayloadException>(() => chips.Query("vegan").Execute());
        chips.Search("vegan", new LuiQueryOptions { MaxResults = 1 }).Hits.ShouldHaveSingleItem().Key.ShouldBe(1L);
    }

    [Fact]
    public void An_unreadable_payload_names_a_text_key()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open(Json());
        var chips = catalog.Create(NamedChips);
        chips.Upsert("alpha", "vegan", new Chip("one"));
        Execute(database, "UPDATE lui_named_chips SET payload = X'7B7D' WHERE key = 'alpha';");

        var exception = Should.Throw<LuiPayloadException>(() => chips.Search("vegan"));

        exception.IndexName.ShouldBe("named_chips");
        exception.Key.ShouldBe("alpha");
        exception.Message.ShouldContain("'alpha'", Case.Sensitive);
    }

    [Fact]
    public void An_entry_without_a_stored_payload_fails_the_query()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open(Json());
        var chips = catalog.Create(Chips);
        chips.Upsert(1, "vegan", new Chip("one"));
        Execute(database, "UPDATE lui_chips SET payload = NULL WHERE key = '1';");

        var exception = Should.Throw<LuiPayloadException>(() => chips.Search("vegan"));

        exception.IndexName.ShouldBe("chips");
        exception.Key.ShouldBe("1");
        exception.RequestedType.ShouldBe(typeof(Chip));
    }

    [Fact]
    public void A_throwing_map_fails_the_query()
    {
        using var database = Prepared();
        using var catalog = database.Open();
        var chips = catalog.Create(
            new LuiLinkedIndexDefinition<long, Chip>(
                "chips",
                index => index.SingleWord().LinkTo("tag", "id", "name", weight: "usage_count")
                    .WithPayload(["slug"], row => string.Equals(row.GetString("slug"), "veganer", StringComparison.Ordinal) ? throw new FormatException("no slug") : new Chip(row.GetString("slug")))));
        chips.Sync();

        var exception = Should.Throw<LuiPayloadException>(() => chips.Search("vegan"));

        exception.IndexName.ShouldBe("chips");
        exception.Key.ShouldBe("2");
        exception.RequestedType.ShouldBe(typeof(Chip));
        exception.InnerException.ShouldBeOfType<FormatException>();
        exception.Message.ShouldContain("'chips'", Case.Sensitive);
        exception.Message.ShouldContain("'2'", Case.Sensitive);
    }

    [Fact]
    public void A_map_returning_null_fails_the_query()
    {
        using var database = Prepared();
        using var catalog = database.Open();
        var chips = catalog.Create(
            new LuiLinkedIndexDefinition<long, Chip>(
                "chips",
                index => index.SingleWord().LinkTo("tag", "id", "name", weight: "usage_count")
                    .WithPayload(["slug"], row => string.Equals(row.GetString("slug"), "veganer", StringComparison.Ordinal) ? null! : new Chip(row.GetString("slug")))));
        chips.Sync();

        var exception = Should.Throw<LuiPayloadException>(() => chips.Search("vegan"));

        exception.IndexName.ShouldBe("chips");
        exception.Key.ShouldBe("2");
        exception.RequestedType.ShouldBe(typeof(Chip));
        exception.Message.ShouldContain("null", Case.Insensitive);
    }

    [Fact]
    public void The_payload_can_be_read_after_the_catalog_is_disposed()
    {
        using var database = Prepared();
        var catalog = database.Open(Json());
        var chips = catalog.Create(Chips);
        chips.Upsert(7, "vegetarisch", new Chip("seven", "green"));
        var linked = catalog.Create(
            new LuiLinkedIndexDefinition<long, Chip>(
                "linked_chips",
                index => index.SingleWord().LinkTo("tag", "id", "name", weight: "usage_count").WithPayload(["slug"], row => new Chip(row.GetString("slug")))));
        linked.Sync();

        var embedded = chips.Search("vegetarisch");
        var built = linked.Search("vegan");
        catalog.Dispose();

        embedded.Hits.ShouldHaveSingleItem().Payload.ShouldBe(new Chip("seven", "green"));
        built.Hits.Select(hit => hit.Payload).ShouldBe([new Chip("vegan"), new Chip("veganer")]);
        Should.Throw<ObjectDisposedException>(() => chips.Search("vegetarisch"));
    }

    [Fact]
    public void A_query_with_payload_checks_the_index_like_any_other_operation()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open(Json());
        var chips = catalog.Create(Chips);
        chips.Upsert(1, "vegan", new Chip("one"));
        chips.Search("vegan").Hits.ShouldHaveSingleItem();

        catalog.DropIndex("chips").ShouldBeTrue();
        Should.Throw<LuiIndexNotFoundException>(() => chips.Search("vegan")).IndexName.ShouldBe("chips");

        // created again with another definition: the handle is not the one of the new index
        var other = catalog.Create(new LuiIndexDefinition<long, Chip>("chips", index => index.MultiWord()));
        Should.Throw<LuiIndexNotFoundException>(() => chips.Search("vegan"), "the failure of a handle is kept");
        other.Upsert(1, "vegan", new Chip("one"));
        other.Search("vegan").Hits.ShouldHaveSingleItem().Payload.ShouldBe(new Chip("one"));
    }

    [Fact]
    public void A_query_with_payload_loads_the_payloads_inside_the_budget_of_a_query()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open(new LuiCatalogOptions { Payloads = new LuiPayloadRegistry().Use(SuggestionJsonContext.Default) });
        var suggestions = catalog.Create(new LuiIndexDefinition<long, SuggestionPayload>("suggestions", index => index.SingleWord()));
        suggestions.UpsertMany(
            Enumerable.Range(1, 200).Select(key => new LuiEntry<long, SuggestionPayload>(
                key,
                $"apfel{key}",
                new SuggestionPayload($"Apfelkuchen Nummer {key}", "Marianne Obstgarten", 45 + key, 4.5, "Kuchen und Torten"))
            { Weight = key }));

        var allocated = OnOwnThread(() => (Measure(suggestions), 0L)).Item1;

        // the budget of a query is 60 KB at the median (ADR-0032); the payloads of twenty hits are part of it
        allocated.ShouldBeLessThan(
            60 * 1024,
            $"A query of twenty hits with a payload of about 200 bytes of JSON allocates {allocated / 1024.0:F1} KB.");
    }

    [Fact]
    public void A_query_without_payload_allocates_nothing_for_payloads()
    {
        // an index that has no declared payload type may still hold bytes in its payload column (written by another program, or left by
        // an older version); a typed handle without a payload type does not read them — the statement of its hits has no payload column,
        // so the bytes are never copied (ADR-0074)
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var plain = catalog.Create(new LuiIndexDefinition<long>("plain", index => index.SingleWord()));
        var bare = catalog.Create(new LuiIndexDefinition<long>("bare", index => index.SingleWord()));
        for (var key = 1; key <= 60; key++)
        {
            plain.Upsert(new LuiEntry<long>(key, $"apfel{key}") { Weight = key });
            bare.Upsert(new LuiEntry<long>(key, $"apfel{key}") { Weight = key });
        }

        Execute(database, "UPDATE lui_plain SET payload = zeroblob(4000);");

        var withStoredPayloads = catalog.Index(Plain);
        var withoutPayloads = catalog.Index(Bare);

        var (stored, none) = OnOwnThread(() => (Measure(withStoredPayloads), Measure(withoutPayloads)));

        // twenty hits of 4 KB each would be 80 KB more: the query of an index without payload type does not carry the bytes
        (stored - none).ShouldBeLessThan(
            2_048,
            $"A query on an index whose entries store 4 KB payloads allocates {stored / 1024.0:F1} KB, one without payloads {none / 1024.0:F1} KB: "
            + "a handle without a payload type must not read the payload column.");
    }

    /// <summary>Runs the action on a thread of its own: the pool of the buffers of the candidates is thread-static.</summary>
    private static (long, long) OnOwnThread(Func<(long, long)> action)
    {
        (long, long) result = default;
        var thread = new Thread(() => result = action());
        thread.Start();
        thread.Join();
        return result;
    }

    /// <summary>The allocation of one query of twenty hits, after a warm-up query with the same options.</summary>
    private static long Measure(LuiIndex<long> index)
    {
        var options = new LuiQueryOptions { MaxResults = 20, TypoTolerance = false };
        index.Search("apfel", options);

        var before = GC.GetAllocatedBytesForCurrentThread();
        index.Search("apfel", options);
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    /// <summary>The same for a handle with a payload.</summary>
    private static long Measure<TPayload>(LuiIndex<long, TPayload> index)
        where TPayload : notnull
    {
        var options = new LuiQueryOptions { MaxResults = 20, TypoTolerance = false };
        index.Search("apfel", options);

        var before = GC.GetAllocatedBytesForCurrentThread();
        index.Search("apfel", options);
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }
}
