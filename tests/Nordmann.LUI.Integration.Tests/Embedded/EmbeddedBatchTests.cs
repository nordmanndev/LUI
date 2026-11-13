using Shouldly;

namespace Nordmann.LUI.Integration.Tests.Embedded;

/// <summary>
/// The batch of an <c>Embedded</c> index (ADR-0016, ADR-0013): <c>UpsertMany</c> writes every entry, in
/// a transaction of its own or in the one of the application, and teaches the typo dictionary as a single upsert does.
/// </summary>
public sealed class EmbeddedBatchTests
{
    // the shape of the API before 0.1.0 (ADR-0013), the batch
    [Fact]
    public void A_batch_writes_every_entry_and_says_how_many()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var tags = catalog.Create("tags", index => index.SingleWord());

        var written = tags.UpsertMany(Enumerable.Range(1, 1_000).Select(
            i => new LuiEntry(i, $"kuchen{i}") { Weight = i, Payload = new Chip($"k{i}") }));

        written.ShouldBe(1_000);
        tags.Count().ShouldBe(1_000);
        tags.Search("kuchen1000").Hits[0].GetPayload<Chip>().ShouldBe(new Chip("k1000"));
    }

    // the shape of the API before 0.1.0 (ADR-0013), the batch
    [Fact]
    public void A_key_that_comes_twice_in_a_batch_keeps_the_last_entry()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var tags = catalog.Create("tags", index => index.SingleWord());

        tags.UpsertMany(
        [
            new LuiEntry(1, "apfel") { Weight = 1 },
            new LuiEntry(1, "birne") { Weight = 2 },
        ]);

        tags.Count().ShouldBe(1);
        tags.Search("apfel").Hits.ShouldBeEmpty();
        tags.Search("birne").Hits.ShouldHaveSingleItem().Weight.ShouldBe(2);
    }

    // the shape of the API before 0.1.0 (ADR-0013), the batch
    [Fact]
    public void A_batch_in_its_own_transaction_writes_all_or_nothing()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var tags = catalog.Create("tags", index => index.SingleWord());

        Should.Throw<InvalidOperationException>(() => tags.UpsertMany(FailingAfter(10)));

        tags.Count().ShouldBe(0);

        // the writer is free again: a single write and another batch go through
        tags.Upsert(1, "apfel", 1);
        tags.UpsertMany([new LuiEntry(2, "birne")]).ShouldBe(1);
        tags.Count().ShouldBe(2);
    }

    // the shape of the API before 0.1.0 (ADR-0013), the batch
    [Fact]
    public void A_cancelled_batch_rolls_its_own_transaction_back()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var tags = catalog.Create("tags", index => index.SingleWord());
        using var cancellation = new CancellationTokenSource();

        IEnumerable<LuiEntry> Entries()
        {
            for (var i = 1; i <= 100; i++)
            {
                if (i == 50)
                {
                    cancellation.Cancel();
                }

                yield return new LuiEntry(i, $"kuchen{i}");
            }
        }

        Should.Throw<OperationCanceledException>(() => tags.UpsertMany(Entries(), cancellationToken: cancellation.Token));

        tags.Count().ShouldBe(0);
    }

    // the shape of the API before 0.1.0 (ADR-0013), the batch
    [Fact]
    public void A_batch_in_the_transaction_of_the_application_is_part_of_it()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var tags = catalog.Create("tags", index => index.SingleWord());

        using (var connection = database.Connect())
        using (var transaction = connection.BeginTransaction())
        {
            tags.UpsertMany([new LuiEntry(1, "apfel"), new LuiEntry(2, "birne")], transaction)
                .ShouldBe(2);
            transaction.Rollback();
        }

        tags.Count().ShouldBe(0);

        using (var connection = database.Connect())
        using (var transaction = connection.BeginTransaction())
        {
            tags.UpsertMany([new LuiEntry(1, "apfel")], transaction);
            transaction.Commit();
        }

        tags.Search("apfel").Hits.ShouldHaveSingleItem();
    }

    // the shape of the API before 0.1.0 (ADR-0013), the batch
    [Fact]
    public void A_batch_teaches_the_typo_dictionary_its_new_words_as_a_single_upsert_does()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var batch = catalog.Create("batch", index => index.SingleWord().TypoTolerance());
        var single = catalog.Create("single", index => index.SingleWord().TypoTolerance());
        foreach (var index in new[] { batch, single })
        {
            index.Upsert(1, "Apfelkuchen", 10);
            index.WarmUpTypoDictionary();
        }

        batch.UpsertMany([new LuiEntry(2, "Himbeere") { Weight = 50 }]);
        single.Upsert(2, "Himbeere", 50);

        var learned = batch.Search("himbere");
        learned.CorrectedQuery.ShouldNotBeNull();
        learned.CorrectedQuery.ShouldBe(single.Search("himbere").CorrectedQuery);
        learned.Hits.ShouldHaveSingleItem().Key.ShouldBe(2);
    }

    // the shape of the API before 0.1.0 (ADR-0013), the batch
    [Fact]
    public async Task A_batch_runs_beside_single_writes_of_other_indexes()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var batch = catalog.Create("batch", index => index.SingleWord());
        var single = catalog.Create("single", index => index.SingleWord());

        var writer = Task.Run(() =>
        {
            for (var i = 1; i <= 200; i++)
            {
                single.Upsert(i, $"torte{i}", i);
            }
        });

        for (var round = 0; round < 10; round++)
        {
            batch.UpsertMany(Enumerable.Range((round * 100) + 1, 100).Select(i => new LuiEntry(i, $"kuchen{i}")));
        }

        await writer.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        batch.Count().ShouldBe(1_000);
        single.Count().ShouldBe(200);
    }

    private static IEnumerable<LuiEntry> FailingAfter(int count)
    {
        for (var i = 1; i <= count; i++)
        {
            yield return new LuiEntry(i, $"kuchen{i}");
        }

        throw new InvalidOperationException("the source failed");
    }
}
