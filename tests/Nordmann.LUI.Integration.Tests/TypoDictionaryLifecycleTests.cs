using Nordmann.LUI.Normalization;
using Shouldly;

namespace Nordmann.LUI.Integration.Tests;

/// <summary>
/// The typo dictionary of an index in its life (#19, ADR-0021, ADR-0025): a query does not wait for a build another thread
/// runs — it answers without correction and says so —, a built dictionary only grows (a sync does not build it anew),
/// <c>RebuildTypoDictionary</c> forgets what is gone, and every index has a dictionary of its own.
/// </summary>
public sealed class TypoDictionaryLifecycleTests
{
    [Fact]
    public void A_correcting_query_leaves_the_dictionary_unbuilt_and_says_it_skipped_the_correction()
    {
        // issue #35, direction 2 (ADR-0042): the query does not build the dictionary. It answers with what the input
        // found and says that it skipped the correction — the build belongs to the application, which asks for it.
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var words = catalog.Create("words", index => index.SingleWord().TypoTolerance());
        words.Upsert(1, "kartoffel");

        var result = words.Search("kartofel");

        result.CorrectionSkipped.ShouldBeTrue();
        result.CorrectedQuery.ShouldBeNull();
        result.Hits.ShouldBeEmpty();
        words.TypoDictionaryState.ShouldBe(LuiTypoDictionaryState.NotBuilt, "the query does not build the dictionary");
    }

    [Fact]
    public async Task A_writer_that_waits_does_not_deadlock_a_query()
    {
        // issue #35: with the WAL switched off, a writer that waits takes PENDING — and the build of the query, on its
        // second connection, cannot read any more. The writer waits for the snapshot of the query, the query waits for
        // its build: both wait for the busy timeout.
        // The query does not build any more (ADR-0042): there is no second connection, and no deadlock.
        using var database = new TestDatabase();
        using var catalog = database.Open(new LuiCatalogOptions { WriteAheadLog = false, BusyTimeout = TimeSpan.FromSeconds(2) });
        var words = catalog.Create("words", index => index.SingleWord().TypoTolerance());
        words.Upsert(1, "kartoffel");

        // the writer waits for the lock of the query
        var writer = Task.Run(() => words.Upsert(2, "tomate", 2));

        var result = words.Search("kartofel");

        result.CorrectionSkipped.ShouldBeTrue();
        words.TypoDictionaryState.ShouldBe(LuiTypoDictionaryState.NotBuilt);
        await writer.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task A_query_does_not_wait_for_a_build_that_runs_and_says_it_skipped_the_correction()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        using var reading = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();

        // the dictionary of "words" is the catalog's, one per index: made here first, with a source that blocks, the index
        // takes this one
        catalog.TypoDictionaries.For("words", new NormalizationOptions(), () => Blocking(reading, release));
        var words = catalog.Create("words", index => index.SingleWord().TypoTolerance());
        words.Upsert(1, "kartoffel");
        words.TypoDictionaryState.ShouldBe(LuiTypoDictionaryState.NotBuilt);

        var warmUp = Task.Run(() => words.WarmUpTypoDictionary());
        reading.Wait(TimeSpan.FromSeconds(10)).ShouldBeTrue("the warm-up reads its source");
        words.TypoDictionaryState.ShouldBe(LuiTypoDictionaryState.Building);

        var during = words.Search("kartofel");
        during.CorrectionSkipped.ShouldBeTrue();
        during.CorrectedQuery.ShouldBeNull();
        words.Search("kartof").CorrectionSkipped.ShouldBeFalse("a query with hits needs no correction, so nothing was skipped");

        release.Set();
        await warmUp.WaitAsync(TimeSpan.FromSeconds(10));

        var after = words.Search("kartofel");
        words.TypoDictionaryState.ShouldBe(LuiTypoDictionaryState.Current);
        after.CorrectionSkipped.ShouldBeFalse();
        after.CorrectedQuery.ShouldBe("kartoffel");
    }

    [Fact]
    public void A_failed_warm_up_says_so_and_the_query_does_not_build_the_dictionary_either()
    {
        // review of pull request 34, I3: a failed build is a correction not searched, not a failed query. Since the query
        // does not build any more (ADR-0042), the failure is where the build was asked for: in the warm-up.
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var fail = true;
        catalog.TypoDictionaries.For("words", new NormalizationOptions(), () => fail ? Failing() : [("kartoffel", 1)]);
        var words = catalog.Create("words", index => index.SingleWord().TypoTolerance());
        words.Upsert(1, "kartoffel");

        Should.Throw<InvalidOperationException>(() => words.WarmUpTypoDictionary());
        words.TypoDictionaryState.ShouldBe(LuiTypoDictionaryState.NotBuilt);

        var query = words.Search("kartofel");
        query.CorrectionSkipped.ShouldBeTrue();
        query.CorrectedQuery.ShouldBeNull();
        words.TypoDictionaryState.ShouldBe(LuiTypoDictionaryState.NotBuilt, "the query does not build the dictionary");

        fail = false;
        words.WarmUpTypoDictionary();
        words.TypoDictionaryState.ShouldBe(LuiTypoDictionaryState.Current);
        words.Search("kartofel").CorrectedQuery.ShouldBe("kartoffel");
    }

    [Fact]
    public void A_cancelled_warm_up_leaves_the_dictionary_unbuilt_and_the_query_does_not_build_it()
    {
        // review of pull request 34, I3: the build is the index's, not the query's — an autocomplete cancels at every
        // keystroke, and a build bound to the request would never finish. Since the query does not build any more
        // (ADR-0042), the build belongs to the caller of the warm-up: a cancelled warm-up leaves the dictionary unbuilt,
        // and a query does not build it either.
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var words = catalog.Create("words", index => index.SingleWord().TypoTolerance());
        words.Upsert(1, "kartoffel");

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Should.Throw<OperationCanceledException>(() => words.WarmUpTypoDictionary(cancelled.Token));
        words.TypoDictionaryState.ShouldBe(LuiTypoDictionaryState.NotBuilt);

        var query = words.Search("kartofel");
        query.CorrectionSkipped.ShouldBeTrue();
        words.TypoDictionaryState.ShouldBe(LuiTypoDictionaryState.NotBuilt, "the query does not build the dictionary");

        words.WarmUpTypoDictionary();
        words.TypoDictionaryState.ShouldBe(LuiTypoDictionaryState.Current);
        words.Search("kartofel").CorrectedQuery.ShouldBe("kartoffel");
    }

    [Fact]
    public void A_sync_teaches_the_dictionary_its_new_words_and_a_removed_word_lingers_until_a_rebuild()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var words = catalog.Create("words", index => index.SingleWord().TypoTolerance());
        words.Sync(Entries("kartoffel"));
        words.WarmUpTypoDictionary();
        words.TypoDictionaryState.ShouldBe(LuiTypoDictionaryState.Current);

        words.Sync(Entries("kartoffel", "tomate"));
        words.TypoDictionaryState.ShouldBe(LuiTypoDictionaryState.Current);
        words.Search("tomahe").CorrectedQuery.ShouldBe("tomate", "a word a sync added is known at once");

        words.Sync(Entries("tomate"));
        words.TypoDictionaryState.ShouldBe(LuiTypoDictionaryState.Current, "there is no stale state (ADR-0025)");
        var lingering = words.Search("kartofel");
        lingering.Hits.ShouldBeEmpty("the index is the filter: the correction finds no candidate and falls away");
        words.Search("tomahe").Hits.ShouldHaveSingleItem().Text.ShouldBe("tomate");

        words.RebuildTypoDictionary();
        words.TypoDictionaryState.ShouldBe(LuiTypoDictionaryState.Current);
        words.Search("kartofel").CorrectedQuery.ShouldBeNull("built anew, the removed word is gone");
    }

    [Fact]
    public void Every_index_has_a_dictionary_of_its_own_and_a_query_is_never_corrected_towards_the_words_of_another()
    {
        // owner's requirement (#48): a search among tags corrects no name, and a search among names corrects no tag
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var tags = catalog.Create("tags", index => index.SingleWord().TypoTolerance());
        var names = catalog.Create("names", index => index.SingleWord().TypoTolerance());
        tags.Sync(Entries("vegan", "glutenfrei"));
        names.Sync(Entries("megan", "mueller"));

        // the application asks for the build (ADR-0042): a query never does
        tags.WarmUpTypoDictionary();
        names.WarmUpTypoDictionary();

        var tagQuery = tags.Search("megn");
        tagQuery.Hits.ShouldBeEmpty("'megan' is a name, not a tag");
        tagQuery.CorrectedQuery.ShouldBeNull();

        var nameQuery = names.Search("vegn");
        nameQuery.Hits.ShouldBeEmpty("'vegan' is a tag, not a name");
        nameQuery.CorrectedQuery.ShouldBeNull();

        tags.Search("vegn").CorrectedQuery.ShouldBe("vega", "the dictionary holds beginnings; the search finds 'vegan' with it");
        tags.Search("vegn").Hits.ShouldHaveSingleItem().Text.ShouldBe("vegan");
        names.Search("megn").Hits.ShouldHaveSingleItem().Text.ShouldBe("megan");
    }

    [Fact]
    public void An_index_without_typo_tolerance_has_a_dictionary_that_is_never_built()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var words = catalog.Create("words", index => index.SingleWord());
        words.Sync(Entries("kartoffel"));

        words.Search("kartofel").CorrectionSkipped.ShouldBeFalse();
        words.TypoDictionaryState.ShouldBe(LuiTypoDictionaryState.NotBuilt);
    }

    private static IEnumerable<LuiEntry> Entries(params string[] texts) =>
        texts.Select((text, i) => new LuiEntry(i + 1, text));

    private static IEnumerable<(string Text, int Weight)> Failing()
    {
        yield return ("tomate", 1);
        throw new InvalidOperationException("the table is gone");
    }

    private static IEnumerable<(string Text, int Weight)> Blocking(ManualResetEventSlim reading, ManualResetEventSlim release)
    {
        reading.Set();
        release.Wait(TimeSpan.FromSeconds(10));
        yield return ("kartoffel", 1);
    }
}
