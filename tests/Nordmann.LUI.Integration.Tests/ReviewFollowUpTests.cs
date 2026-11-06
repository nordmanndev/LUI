using Microsoft.Data.Sqlite;
using Shouldly;

namespace Nordmann.LUI.Integration.Tests;

/// <summary>
/// The findings of the review of pull request 6, as tests: each one was
/// reproduced red before it was fixed.
/// </summary>
public sealed class ReviewFollowUpTests
{
    // ---- B1: a value-type payload ------------------------------------------------------------

    [Fact]
    public void An_index_without_a_payload_accepts_an_upsert_whose_payload_is_a_value_type()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var nums = catalog.Create("nums", index => index.SingleWord());

        Should.NotThrow(() => nums.Upsert(1, "vegan", 5));

        nums.Query("vegan").Execute().Hits.ShouldHaveSingleItem().Payload.ShouldBe(default(LuiNoPayload));
    }

    [Fact]
    public void A_value_type_payload_is_stored_unless_it_is_the_default()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var nums = catalog.Create("nums", index => index.SingleWord().WithPayload(new IntSerializer()));

        nums.Upsert(new(1, "vegan") { Weight = 5, Payload = 42 });
        nums.Upsert(2, "vegane torte", 4);

        var hits = nums.Query("vegan").Rank(LuiRankingOrder.WeightOnly).Execute().Hits;
        hits[0].Payload.ShouldBe(42);
        hits[1].Payload.ShouldBe(0);
        database.Scalar<long>("SELECT count(*) FROM lui_nums WHERE payload IS NULL").ShouldBe(1);
    }

    // ---- B2: one snapshot for the candidates and the hits ------------------------------------

    [Fact]
    public void The_hits_are_read_in_the_snapshot_of_the_candidates()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var tags = catalog.Create("tags", index => index.SingleWord().WithPayload(ChipJsonContext.Default.Chip));
        tags.Upsert(new(1, "kirsche kuchen") { Weight = 10, Payload = new Chip("old1") });
        tags.Upsert(new(2, "zitrone kuchen") { Weight = 5, Payload = new Chip("old2") });

        // between the candidates and the hits, another writer removes the highest document and
        // adds a new one: SQLite hands the freed doc_id to the newcomer
        var interference = new OnFirstCall(() =>
        {
            tags.Remove(2);
            tags.Upsert(new(3, "himbeer kuchen") { Weight = 5, Payload = new Chip("new3") });
        });

        var hits = tags.Query("kuchen").Similarity(interference).Execute().Hits;

        interference.Called.ShouldBeTrue();
        foreach (var hit in hits)
        {
            (hit.Text, hit.Key).ShouldBeOneOf(("kirsche kuchen", 1L), ("zitrone kuchen", 2L));
            hit.Payload!.Slug.ShouldBe(hit.Key == 1 ? "old1" : "old2");
        }
    }

    // ---- B3: the hash ------------------------------------------------------------------------

    [Theory]
    [InlineData("a")]
    [InlineData("ap")]
    [InlineData("apf")]
    [InlineData("apfe")]
    public void A_leading_hash_of_the_input_does_not_change_the_answer(string typed)
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var tags = catalog.Create("tags", index => index.SingleWord());
        tags.Upsert(1, "apfel", 10);
        tags.Upsert(2, "#apfelkuchen", 20);
        tags.Upsert(3, "birne", 30);

        var without = tags.Query(typed).Rank(LuiRankingOrder.WeightOnly).Execute().Hits.Select(h => h.Key).ToArray();
        var with = tags.Query("#" + typed).Rank(LuiRankingOrder.WeightOnly).Execute().Hits.Select(h => h.Key).ToArray();

        without.ShouldBe([2, 1]);
        with.ShouldBe(without);
    }

    // ---- B4: code points, not UTF-16 units ---------------------------------------------------

    [Fact]
    public void An_input_is_measured_in_code_points()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var tags = catalog.Create("tags", index => index.SingleWord());
        tags.Upsert(1, "😀party", 5);
        tags.Upsert(2, "😀smile", 3);

        tags.Query("😀").Rank(LuiRankingOrder.WeightOnly).Execute().Hits.Select(h => h.Key).ShouldBe([1, 2]);
        tags.Query("😀p").Execute().Hits.ShouldHaveSingleItem().Key.ShouldBe(1);
        tags.Query("😀pa").Execute().Hits.ShouldHaveSingleItem().Key.ShouldBe(1);
    }

    // ---- B5: Upsert replaces the whole entry -------------------------------------------------

    [Fact]
    public void Upsert_replaces_the_whole_entry_including_the_payload()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var tags = catalog.Create("tags", index => index.SingleWord().WithPayload(ChipJsonContext.Default.Chip));
        tags.Upsert(new(1, "vegan") { Weight = 5, Payload = new Chip("vegan") });

        tags.Upsert(1, "vegan", 5);

        tags.Query("vegan").Execute().Hits.ShouldHaveSingleItem().Payload.ShouldBeNull();
    }

    // ---- B6: the journal mode ----------------------------------------------------------------

    [Fact]
    public void A_database_can_stay_in_its_journal_mode()
    {
        using var database = new TestDatabase();
        using (var connection = database.Connect())
        {
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA journal_mode = DELETE; CREATE TABLE app(id INTEGER PRIMARY KEY);";
            command.ExecuteNonQuery();
        }

        using var catalog = database.Open(new LuiCatalogOptions { WriteAheadLog = false });
        var tags = catalog.Create("tags", index => index.SingleWord());
        tags.Upsert(1, "vegan", 5);

        database.Scalar<string>("PRAGMA journal_mode").ShouldBe("delete");
        tags.Query("vegan").Execute().Hits.Count.ShouldBe(1);
    }

    [Fact]
    public void A_database_in_rollback_mode_is_switched_to_wal_by_default()
    {
        using var database = new TestDatabase();
        using (var connection = database.Connect())
        {
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA journal_mode = DELETE; CREATE TABLE app(id INTEGER PRIMARY KEY);";
            command.ExecuteNonQuery();
        }

        using var catalog = database.Open();

        database.Scalar<string>("PRAGMA journal_mode").ShouldBe("wal");
    }

    // ---- B8: a transaction of another database ----------------------------------------------

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

    // ---- the version check ------------------------------------------------------------------

    [Fact]
    public void A_sqlite_that_is_too_old_is_named_precisely()
    {
        var exception = Should.Throw<LuiEnvironmentException>(() => LuiCatalog.EnsureSqliteVersion("3.44.0"));
        exception.Message.ShouldContain("3.45");
        exception.Message.ShouldContain("3.44.0");

        Should.NotThrow(() => LuiCatalog.EnsureSqliteVersion("3.45.0"));
        Should.NotThrow(() => LuiCatalog.EnsureSqliteVersion("3.53.3"));
    }

    // ---- doc_id exhaustion -----------------------------------------------------------------

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

    // ---- P1: a write that changes nothing leaves the search index alone ------------------------

    [Fact]
    public void An_identical_upsert_does_not_touch_the_search_index()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var tags = catalog.Create("tags", index => index.SingleWord().WithPayload(ChipJsonContext.Default.Chip));
        tags.Upsert(new(1, "vegan") { Weight = 5, Payload = new Chip("v") });
        tags.Upsert(new(2, "vegane torte") { Weight = 4, Payload = new Chip("t") });
        var before = SearchIndexFingerprint(database);

        for (var i = 0; i < 50; i++)
        {
            tags.Upsert(new(1, "vegan") { Weight = 5, Payload = new Chip("v") });
        }

        SearchIndexFingerprint(database).ShouldBe(before);
    }

    [Fact]
    public void A_payload_only_change_does_not_touch_the_search_index_and_the_payload_arrives()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var tags = catalog.Create("tags", index => index.SingleWord().WithPayload(ChipJsonContext.Default.Chip));
        tags.Upsert(new(1, "vegan") { Weight = 5, Payload = new Chip("v") });
        var before = SearchIndexFingerprint(database);

        tags.Upsert(new(1, "vegan") { Weight = 5, Payload = new Chip("changed") });
        tags.UpdateWeight(1, 5);
        tags.UpdatePriority(1, LuiPriority.Normal);

        SearchIndexFingerprint(database).ShouldBe(before);
        tags.Query("vegan").Execute().Hits.ShouldHaveSingleItem().Payload.ShouldBe(new Chip("changed"));
    }

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

    // ---- P6: cancellation while ranking ------------------------------------------------------

    [Fact]
    public void A_query_can_be_cancelled_while_it_is_being_ranked()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var tags = catalog.Create("tags", index => index.SingleWord());
        using (var connection = database.Connect())
        {
            using var transaction = connection.BeginTransaction();
            for (var i = 0; i < 2_000; i++)
            {
                tags.Upsert(i, $"kuchen{i:0000}", i, transaction: transaction);
            }

            transaction.Commit();
        }

        using var cancellation = new CancellationTokenSource();
        var similarity = new OnFirstCall(cancellation.Cancel);

        Should.Throw<OperationCanceledException>(
            () => tags.Query("kuchen").Candidates(2_000).Take(5).Similarity(similarity).Execute(cancellation.Token));
    }

    private static string SearchIndexFingerprint(TestDatabase database) =>
        database.Scalar<string>(
            "SELECT count(*) || ':' || coalesce(sum(length(block)), 0) || ':' || coalesce(sum(id), 0) FROM lui_tags_fts_data");

    private sealed class OnFirstCall(Action action) : ILuiSimilarity
    {
        private int _calls;

        internal bool Called => Volatile.Read(ref _calls) > 0;

        public double Similarity(string query, string text)
        {
            if (Interlocked.Increment(ref _calls) == 1)
            {
                action();
            }

            return 0.5;
        }
    }

    private sealed class IntSerializer : ILuiPayloadSerializer<int>
    {
        public byte[] Serialize(int payload) => BitConverter.GetBytes(payload);

        public int Deserialize(ReadOnlySpan<byte> bytes) => BitConverter.ToInt32(bytes);
    }
}
