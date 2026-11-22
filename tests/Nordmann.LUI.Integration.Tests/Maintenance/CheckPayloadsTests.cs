#pragma warning disable MA0048 // the payload types of these tests, one per shape of the same stored name, and the JSON contexts that know them
using System.Text.Json.Serialization;
using Nordmann.LUI.Integration.Tests.Embedded;
using Shouldly;
using static Nordmann.LUI.Integration.Tests.Linked.LinkedTable;

namespace Nordmann.LUI.Integration.Tests.Maintenance;

/// <summary>A dish as the first release of an application stored it.</summary>
/// <param name="Title">The title.</param>
/// <param name="Minutes">The minutes.</param>
[LuiPayloadName("dish")]
public sealed record DishWithMinutes(string Title, int Minutes);

/// <summary>The same dish after a field was removed: the same stored name, so the index opens — and its old payloads do not read.</summary>
/// <param name="Title">The title.</param>
[LuiPayloadName("dish")]
public sealed record DishWithoutMinutes(string Title);

/// <summary>The same dish after a field was added with a default value: old payloads still read.</summary>
/// <param name="Title">The title.</param>
/// <param name="Minutes">The minutes.</param>
/// <param name="Rating">The rating the first release did not know.</param>
[LuiPayloadName("dish")]
public sealed record DishWithRating(string Title, int Minutes, double Rating = 0);

/// <summary>The dish of the new index: another stored name, so it lives beside the old one.</summary>
/// <param name="Title">The title.</param>
[LuiPayloadName("dish_v2")]
public sealed record DishOfTheNewIndex(string Title);

/// <summary>The JSON context of <see cref="DishWithMinutes"/> and of the dish of the new index.</summary>
[JsonSerializable(typeof(DishWithMinutes))]
[JsonSerializable(typeof(DishOfTheNewIndex))]
public sealed partial class DishFirstJsonContext : JsonSerializerContext;

/// <summary>The JSON context of <see cref="DishWithoutMinutes"/>.</summary>
[JsonSerializable(typeof(DishWithoutMinutes))]
public sealed partial class DishWithoutMinutesJsonContext : JsonSerializerContext;

/// <summary>The JSON context of <see cref="DishWithRating"/>.</summary>
[JsonSerializable(typeof(DishWithRating))]
public sealed partial class DishWithRatingJsonContext : JsonSerializerContext;

/// <summary>
/// Finding the payloads that cannot be read (ADR-0074, issue #262): <c>CheckPayloads()</c> is a full read of the index and lists
/// the entries by key, <c>Check()</c> reads no payload, and the way a payload type changes is a matter of the entries being written
/// again — or of a new index.
/// </summary>
public sealed class CheckPayloadsTests
{
    private const string Corrupt = "X'7B226F6C64223A317D'";

    private static readonly LuiIndexDefinition<long, Chip> Chips = new("chips", index => index.SingleWord());

    private static readonly LuiIndexDefinition<string, Chip> NamedChips = new("named_chips", index => index.SingleWord());

    private static readonly LuiIndexDefinition<long, DishWithMinutes> FirstDishes = new("dishes", index => index.SingleWord());

    private static readonly LuiIndexDefinition<long, DishWithoutMinutes> LeanDishes = new("dishes", index => index.SingleWord());

    private static readonly LuiIndexDefinition<long, DishWithRating> RatedDishes = new("dishes", index => index.SingleWord());

    private static readonly LuiIndexDefinition<long, DishOfTheNewIndex> NewDishes = new("dishes_v2", index => index.SingleWord());

    private static readonly string[] Pastas = ["pasta", "pastrami", "pavlova", "paella", "panna", "pancake"];

    private static LuiCatalogOptions Json() => new() { Payloads = new LuiPayloadRegistry().Use(ChipJsonContext.Default) };

    private static void Execute(TestDatabase database, string sql)
    {
        using var connection = database.Connect();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static LuiEntry<long, DishWithMinutes>[] FirstEntries() =>
        [.. Pastas.Select((text, i) => new LuiEntry<long, DishWithMinutes>(i + 1, text, new DishWithMinutes("Dish " + text, 10 + i)) { Weight = 100 - i })];

    private static LuiEntry<long, DishWithoutMinutes>[] LeanEntries() =>
        [.. Pastas.Select((text, i) => new LuiEntry<long, DishWithoutMinutes>(i + 1, text, new DishWithoutMinutes("Dish " + text)) { Weight = 100 - i })];

    /// <summary>An index of six dishes in the shape of the first release, written by a catalog that knows only that shape.</summary>
    private static void WriteTheFirstRelease(TestDatabase database)
    {
        using var first = database.Open(new LuiCatalogOptions { Payloads = new LuiPayloadRegistry().Use(DishFirstJsonContext.Default) });
        first.Create(FirstDishes).UpsertMany(FirstEntries()).ShouldBe(6);
    }

    [Fact]
    public void CheckPayloads_lists_every_unreadable_entry_by_key()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open(Json());
        var chips = catalog.Create(Chips);
        chips.UpsertMany(Enumerable.Range(1, 5).Select(key => new LuiEntry<long, Chip>(key, "vegan" + key, new Chip("chip" + key))));
        chips.CheckPayloads().IsHealthy.ShouldBeTrue();
        chips.CheckPayloads().Unreadable.ShouldBeEmpty();

        Execute(database, $"UPDATE lui_chips SET payload = {Corrupt} WHERE key IN ('2', '4');");
        Execute(database, "UPDATE lui_chips SET payload = NULL WHERE key = '5';");

        var check = chips.CheckPayloads();

        check.IsHealthy.ShouldBeFalse();
        check.Unreadable.Select(entry => entry.Key).ShouldBe([2L, 4L, 5L]);
        check.Unreadable.ShouldAllBe(entry => entry.Message.Contains("'chips'", StringComparison.Ordinal) && entry.Message.Contains(typeof(Chip).FullName!, StringComparison.Ordinal));
        check.Unreadable[0].Message.ShouldContain("'2'", Case.Sensitive);
        check.Unreadable[2].Message.ShouldContain("no stored payload", Case.Insensitive);
    }

    [Fact]
    public void CheckPayloads_lists_the_keys_in_the_type_of_the_index()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open(Json());
        var chips = catalog.Create(NamedChips);
        chips.UpsertMany([new LuiEntry<string, Chip>("alpha", "vegan", new Chip("one")), new LuiEntry<string, Chip>("beta", "veganer", new Chip("two"))]);
        Execute(database, $"UPDATE lui_named_chips SET payload = {Corrupt} WHERE key = 'beta';");

        LuiPayloadCheck<string> check = chips.CheckPayloads();

        LuiUnreadablePayload<string> entry = check.Unreadable.ShouldHaveSingleItem();
        entry.Key.ShouldBe("beta");
        entry.Message.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void After_a_rewrite_the_entry_is_gone_from_the_list_and_the_query_succeeds()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open(Json());
        var chips = catalog.Create(Chips);
        chips.UpsertMany(Enumerable.Range(1, 3).Select(key => new LuiEntry<long, Chip>(key, "vegan" + key, new Chip("chip" + key))));
        Execute(database, $"UPDATE lui_chips SET payload = {Corrupt} WHERE key IN ('1', '3');");
        Should.Throw<LuiPayloadException>(() => chips.Search("vegan"));
        chips.CheckPayloads().Unreadable.Select(entry => entry.Key).ShouldBe([1L, 3L]);

        chips.Upsert(1, "vegan1", new Chip("again"));

        chips.CheckPayloads().Unreadable.Select(entry => entry.Key).ShouldBe([3L]);
        chips.Sync(Enumerable.Range(1, 3).Select(key => new LuiEntry<long, Chip>(key, "vegan" + key, new Chip("chip" + key)))).Changed.ShouldBeGreaterThan(0);
        chips.CheckPayloads().IsHealthy.ShouldBeTrue();
        chips.Search("vegan").Hits.Select(hit => hit.Payload).ShouldBe([new Chip("again"), new Chip("chip2"), new Chip("chip3")], ignoreOrder: true);
    }

    [Fact]
    public void Check_reads_no_payload()
    {
        using var database = new TestDatabase();
        var serializer = new LinesChipSerializer();
        using var catalog = database.Open(new LuiCatalogOptions { Payloads = new LuiPayloadRegistry().Use(serializer) });
        var chips = catalog.Create(Chips);
        LuiEntry<long, Chip>[] entries = [.. Enumerable.Range(1, 4).Select(key => new LuiEntry<long, Chip>(key, "vegan" + key, new Chip("chip" + key)))];
        chips.UpsertMany(entries);
        Execute(database, $"UPDATE lui_chips SET payload = {Corrupt} WHERE key = '2';");

        chips.Check().IsHealthy.ShouldBeTrue("the structural check does not look at a payload");
        chips.Check(entries).Different.ShouldBeGreaterThan(0, "the check against a source compares the bytes, it does not read them");
        serializer.Deserialized.ShouldBe(0);

        serializer.Deserialized.ShouldBe(0);
        _ = chips.CheckPayloads();
        serializer.Deserialized.ShouldBe(4, "a full read: every stored payload once");
    }

    [Fact]
    public void A_linked_check_calls_no_map()
    {
        using var database = new TestDatabase();
        Run(database, Table);
        Run(database, "INSERT INTO tag(id, name, usage_count, slug) VALUES (1, 'vegan', 5, 'vegan'), (2, 'veganer', 4, 'veganer');");
        using var catalog = database.Open();
        var calls = 0;
        var chips = catalog.Create(
            new LuiLinkedIndexDefinition<long, Chip>(
                "linked_chips",
                index => index.SingleWord().LinkTo("tag", "id", "name", weight: "usage_count")
                    .WithPayload(["slug"], row =>
                    {
                        Interlocked.Increment(ref calls);
                        return new Chip(row.GetString("slug"));
                    })));
        chips.Sync();
        calls = 0;

        chips.Check().IsHealthy.ShouldBeTrue();

        calls.ShouldBe(0, "the check of a linked index calls no map");
        typeof(LuiLinkedIndex<long, Chip>).GetMethod("CheckPayloads").ShouldBeNull("the payload of a linked index is not stored");
    }

    [Fact]
    public void A_removed_field_fails_old_entries_until_rewritten()
    {
        using var database = new TestDatabase();
        WriteTheFirstRelease(database);

        // the application is deployed with the new type: the name of the payload type is the same, so the index opens
        using var catalog = database.Open(new LuiCatalogOptions { Payloads = new LuiPayloadRegistry().Use(DishWithoutMinutesJsonContext.Default) });
        var dishes = catalog.Index(LeanDishes);

        Should.Throw<LuiPayloadException>(() => dishes.Search("pa"), "every query whose result holds an old entry fails — the search is down").IndexName.ShouldBe("dishes");
        dishes.CheckPayloads().Unreadable.Select(entry => entry.Key).ShouldBe([1L, 2L, 3L, 4L, 5L, 6L]);

        // in place: two entries with Upsert, one batch with UpsertMany, the rest with Sync
        dishes.Upsert(1, "pasta", new DishWithoutMinutes("Dish pasta"), weight: 100);
        dishes.UpsertMany(LeanEntries().Where(entry => entry.Key is 2 or 3));
        dishes.CheckPayloads().Unreadable.Select(entry => entry.Key).ShouldBe([4L, 5L, 6L]);
        dishes.Sync(LeanEntries()).Changed.ShouldBe(3);

        dishes.CheckPayloads().IsHealthy.ShouldBeTrue();
        dishes.Search("pa").Hits.Select(hit => hit.Payload.Title).ShouldContain("Dish pasta");
    }

    [Fact]
    public void A_field_added_with_a_default_keeps_old_entries_readable()
    {
        using var database = new TestDatabase();
        WriteTheFirstRelease(database);

        using var catalog = database.Open(new LuiCatalogOptions { Payloads = new LuiPayloadRegistry().Use(DishWithRatingJsonContext.Default) });
        var dishes = catalog.Index(RatedDishes);

        dishes.CheckPayloads().IsHealthy.ShouldBeTrue();
        var hits = dishes.Search("pasta").Hits;
        hits.ShouldNotBeEmpty();
        hits[0].Payload.ShouldBe(new DishWithRating("Dish pasta", 10, 0));
    }

    [Fact]
    public void The_new_index_way_works_without_a_failing_query()
    {
        using var database = new TestDatabase();
        WriteTheFirstRelease(database);

        // the application knows both shapes while it moves: the old index under the old type, the new one under another stored name
        using var catalog = database.Open(new LuiCatalogOptions { Payloads = new LuiPayloadRegistry().Use(DishFirstJsonContext.Default) });
        var old = catalog.Index(FirstDishes);
        old.Search("pa").Hits.ShouldNotBeEmpty();

        var fresh = catalog.Create(NewDishes);
        fresh.Sync(old.Search("pa", new LuiQueryOptions { MaxResults = 100 }).Hits
            .Select(hit => new LuiEntry<long, DishOfTheNewIndex>(hit.Key, hit.Text, new DishOfTheNewIndex(hit.Payload.Title)) { Weight = hit.Weight })).Added.ShouldBe(6);

        // switched over: the old index may go, the new one answers, and no query failed on the way
        fresh.Search("pa").Hits.Count.ShouldBe(6);
        catalog.DropIndex("dishes").ShouldBeTrue();
        fresh.Search("pasta").Hits[0].Payload.ShouldBe(new DishOfTheNewIndex("Dish pasta"));
        fresh.CheckPayloads().IsHealthy.ShouldBeTrue();
        Should.Throw<LuiIndexNotFoundException>(() => old.Search("pa")).IndexName.ShouldBe("dishes");
    }

    [Fact]
    public void CheckPayloads_checks_the_index_like_any_other_operation()
    {
        using var database = new TestDatabase();
        var catalog = database.Open(Json());
        var chips = catalog.Create(Chips);
        chips.Upsert(1, "vegan", new Chip("one"));

        // the index is created again with another definition: the handle is not the one of the new index
        catalog.DropIndex("chips").ShouldBeTrue();
        Should.Throw<LuiIndexNotFoundException>(() => chips.CheckPayloads()).IndexName.ShouldBe("chips");
        catalog.Create(new LuiIndexDefinition<long, Chip>("chips", index => index.MultiWord()));
        Should.Throw<LuiIndexNotFoundException>(() => chips.CheckPayloads(), "the failure of a handle is kept");

        var other = catalog.Index(new LuiIndexDefinition<long, Chip>("chips", index => index.MultiWord()));
        other.CheckPayloads().IsHealthy.ShouldBeTrue();
        Should.Throw<LuiDefinitionMismatchException>(() => catalog.Index(Chips));

        catalog.Dispose();
        Should.Throw<ObjectDisposedException>(() => other.CheckPayloads());
    }

    [Fact]
    public void CheckPayloads_answers_for_the_index_it_checked_or_throws_and_never_reads_the_new_one()
    {
        using var database = new TestDatabase();
        using var first = database.Open(Json());
        using var second = database.Open(Json());
        first.Create(Chips).Upsert(1, "vegan", new Chip("one"));
        var chips = second.Index(Chips);
        _ = chips.Count();

        // the swap — a drop and a create with another definition, by another catalog — runs between the check and the read
        Task? swap = null;
        second.Database.AfterSchemaCheck = () =>
        {
            second.Database.AfterSchemaCheck = null;
            swap = Task.Run(() =>
            {
                first.DropIndex("chips");
                first.Create(new LuiIndexDefinition<long, Chip>("chips", index => index.MultiWord())).Upsert(2, "other", new Chip("two"));
            });
            _ = swap.Wait(TimeSpan.FromMilliseconds(400));
        };

        LuiPayloadCheck<long>? check = null;
        var failure = Record.Exception(() => check = chips.CheckPayloads());
        second.Database.AfterSchemaCheck = null;
        (swap ?? throw new InvalidOperationException("The operation did not check the schema counter: the seam did not run.")).Wait(TimeSpan.FromSeconds(30)).ShouldBeTrue();

        (failure is null or LuiIndexNotFoundException or LuiDefinitionMismatchException).ShouldBeTrue(failure?.ToString());
        check?.IsHealthy.ShouldBeTrue("the entries of the snapshot the check was made in");
    }

    [Fact]
    public void CheckPayloads_honours_the_cancellation_token_and_a_read_only_catalog_may_call_it()
    {
        using var database = new TestDatabase();
        using (var catalog = database.Open(Json()))
        {
            catalog.Create(Chips).UpsertMany(Enumerable.Range(1, 5).Select(key => new LuiEntry<long, Chip>(key, "vegan" + key, new Chip("chip" + key))));
        }

        using var source = new CancellationTokenSource();
        var stopping = new LuiPayloadRegistry().Use(new CancellingSerializer(source, after: 2));
        using var reading = LuiCatalog.OpenReadOnly(database.FilePath, new LuiCatalogOptions { Payloads = stopping });
        var chips = reading.Index(Chips);

        Should.Throw<OperationCanceledException>(() => chips.CheckPayloads(new CancellationToken(canceled: true)));
        Should.Throw<OperationCanceledException>(() => chips.CheckPayloads(source.Token));
        source.IsCancellationRequested.ShouldBeTrue("the token was cancelled by the second payload, and the third was not read");

        using var again = LuiCatalog.OpenReadOnly(database.FilePath, Json());
        again.Index(Chips).CheckPayloads().IsHealthy.ShouldBeTrue();
    }

    /// <summary>A serializer of the chips that cancels a token while it reads the payload number <c>after</c>.</summary>
    private sealed class CancellingSerializer(CancellationTokenSource source, int after) : ILuiPayloadSerializer<Chip>
    {
        private readonly ILuiPayloadSerializer<Chip> _json = LuiPayloadSerializer.Json<Chip>(ChipJsonContext.Default);
        private int _read;

        public string Format => _json.Format;

        public byte[] Serialize(Chip payload) => _json.Serialize(payload);

        public Chip Deserialize(ReadOnlySpan<byte> bytes)
        {
            if (++_read == after)
            {
                source.Cancel();
            }

            return _json.Deserialize(bytes);
        }
    }
}
