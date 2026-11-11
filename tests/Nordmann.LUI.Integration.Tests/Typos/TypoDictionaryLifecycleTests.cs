using Nordmann.LUI.Normalization;
using Nordmann.LUI.Typos;
using Shouldly;

namespace Nordmann.LUI.Integration.Tests.Typos;

/// <summary>
/// The typo dictionary of an index in its life (#19, ADR-0021, ADR-0025): a query does not wait for a build another thread
/// runs — it answers without correction and says so —, a built dictionary only grows (a sync does not build it anew),
/// <c>RebuildTypoDictionary</c> forgets what is gone, and every index has a dictionary of its own.
/// </summary>
public sealed class TypoDictionaryLifecycleTests
{
    private static readonly NormalizationOptions Normalization = NormalizationOptions.Default;

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
        await writer.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
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
        catalog.TypoDictionaries.For("words", new NormalizationOptions(), () => Blocking(reading, release), LuiTypoDistance.OneError);
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
        await warmUp.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

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
        catalog.TypoDictionaries.For("words", new NormalizationOptions(), () => fail ? Failing() : [("kartoffel", 1)], LuiTypoDistance.OneError);
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
    public void An_index_without_typo_tolerance_has_a_dictionary_that_a_query_never_builds()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var words = catalog.Create("words", index => index.SingleWord());
        words.Sync(Entries("kartoffel"));

        words.Search("kartofel").CorrectionSkipped.ShouldBeFalse();
        words.TypoDictionaryState.ShouldBe(LuiTypoDictionaryState.NotBuilt);
    }

    // review of pull request 10, M1: the build does not hold up the writers, and a rebuild loses nothing
    [Fact]
    [Trait("Category", "Timing")]
    public async Task A_write_is_not_held_up_while_the_dictionary_is_being_built()
    {
        using var building = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var dictionary = new TypoDictionary(Normalization, () => Slow(building, release, [("Apfelkuchen", 10)]), LuiTypoDistance.OneError);

        var build = Task.Run(() => dictionary.EnsureBuilt(CancellationToken.None));
        building.Wait(TimeSpan.FromSeconds(10)).ShouldBeTrue("the build started");

        try
        {
            var add = Task.Run(() => dictionary.Add("Zwetschge", 5));

            // the build is held until the test lets it go: a write that waited for it would wait for ever. The ten
            // seconds are room for a loaded runner, not a measurement (issue #78).
            (await Task.WhenAny(add, Task.Delay(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken))).ShouldBe(add, "a write must not wait for the build");
        }
        finally
        {
            release.Set();
        }

        await build;

        // the word written during the build is in the dictionary afterwards
        dictionary.Corrections("zwetchge").Contains("zwetschge", StringComparer.Ordinal).ShouldBeTrue();
    }

    // review of pull request 10, M1: the build does not hold up the writers, and a rebuild loses nothing
    [Fact]
    public async Task A_word_written_during_a_rebuild_is_not_lost_with_the_swap()
    {
        using var building = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var slow = false;
        var dictionary = new TypoDictionary(
            Normalization,
            () => slow ? Slow(building, release, [("Apfelkuchen", 10)]) : [("Apfelkuchen", 10)],
            LuiTypoDistance.OneError);
        dictionary.EnsureBuilt(CancellationToken.None);

        slow = true;
        var rebuild = Task.Run(() => dictionary.Rebuild(CancellationToken.None));
        building.Wait(TimeSpan.FromSeconds(10)).ShouldBeTrue("the rebuild started");

        try
        {
            dictionary.Add("Zwetschge", 5);
        }
        finally
        {
            release.Set();
        }

        await rebuild;

        dictionary.Corrections("zwetchge").Contains("zwetschge", StringComparer.Ordinal).ShouldBeTrue();
    }

    // issue #107 (the coverage of #80, G6): a batch of texts — what Sync, UpsertMany and ApplyPending write — during a
    // rebuild. The single word is above; the batch takes its own path (AddRange) and was run by no test.
    [Fact]
    public async Task A_batch_written_during_a_rebuild_is_not_lost_with_the_swap()
    {
        using var building = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var slow = false;
        var dictionary = new TypoDictionary(
            Normalization,
            () => slow ? Slow(building, release, [("Apfelkuchen", 10)]) : [("Apfelkuchen", 10)],
            LuiTypoDistance.OneError);
        dictionary.EnsureBuilt(CancellationToken.None);

        slow = true;
        var rebuild = Task.Run(() => dictionary.Rebuild(CancellationToken.None), TestContext.Current.CancellationToken);
        building.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken).ShouldBeTrue("the rebuild started");

        try
        {
            dictionary.AddRange([("Zwetschge", 5), ("Holunder", 3)]);
        }
        finally
        {
            release.Set();
        }

        await rebuild;

        dictionary.Corrections("zwetchge").Contains("zwetschge", StringComparer.Ordinal).ShouldBeTrue("the first text of the batch");
        dictionary.Corrections("holunedr").Contains("holunder", StringComparer.Ordinal).ShouldBeTrue("the last text of the batch");
    }

    // review of pull request 10, M1: the build does not hold up the writers, and a rebuild loses nothing
    [Fact]
    public void A_write_to_a_dictionary_that_is_not_built_is_free_and_forgotten()
    {
        var dictionary = new TypoDictionary(Normalization, () => [("Apfelkuchen", 10)], LuiTypoDistance.OneError);

        dictionary.Add("Zwetschge", 5);

        dictionary.IsBuilt.ShouldBeFalse("a write does not build the dictionary");
        dictionary.Corrections("zwetchge").ShouldBeEmpty();
    }

    // review of pull request 10, M1: the build does not hold up the writers, and a rebuild loses nothing
    [Fact]
    public async Task A_query_that_is_cancelled_during_the_build_leaves_the_dictionary_clean_for_the_next()
    {
        using var building = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var cancellation = new CancellationTokenSource();
        var first = true;
        var dictionary = new TypoDictionary(
            Normalization,
            () =>
            {
                if (first)
                {
                    first = false;
                    return Slow(building, release, [("Apfelkuchen", 10), ("Birnentorte", 5), ("Zitronenkuchen", 3)]);
                }

                return [("Apfelkuchen", 10)];
            },
            LuiTypoDistance.OneError);

        var attempt = Task.Run(() => dictionary.EnsureBuilt(cancellation.Token));
        building.Wait(TimeSpan.FromSeconds(10)).ShouldBeTrue();
        await cancellation.CancelAsync();
        release.Set();

        await Should.ThrowAsync<OperationCanceledException>(() => attempt);
        dictionary.IsBuilt.ShouldBeFalse();

        dictionary.EnsureBuilt(CancellationToken.None);
        dictionary.IsBuilt.ShouldBeTrue();
        dictionary.Corrections("apflekuchen").ShouldNotBeEmpty();
    }

    // review of pull request 10, S1: one dictionary per index of a catalog
    [Fact]
    public void Two_instances_of_one_index_share_the_dictionary()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var reader = catalog.Create("words", index => index.SingleWord().TypoTolerance());
        reader.Upsert(1, "Apfelkuchen", 10);
        reader.WarmUpTypoDictionary();   // the application builds the dictionary (ADR-0042)

        // the distance is part of the definition: the second instance describes the index the same way
        var importer = catalog.Create("words", index => index.SingleWord().TypoTolerance());
        importer.Upsert(2, "Zwetschge", 5);

        var result = reader.Query("zwetchge").Execute();
        result.CorrectedQuery.ShouldBe("zwetschge");
        result.Hits.ShouldHaveSingleItem().Key.ShouldBe(2);
    }

    // review of pull request 10, S1: one dictionary per index of a catalog
    [Fact]
    public void A_dropped_index_does_not_leave_its_words_to_the_next_one_of_that_name()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var first = catalog.Create("words", index => index.SingleWord().TypoTolerance());
        first.Upsert(1, "Apfelkuchen", 10);
        first.WarmUpTypoDictionary();
        first.Query("apflekuchen").Execute().CorrectedQuery.ShouldBe("apfelkuchen");

        catalog.DropIndex("words");
        var second = catalog.Create("words", index => index.SingleWord().TypoTolerance());
        second.Upsert(1, "Birnentorte", 10);

        var result = second.Query("apflekuchen").Execute();
        result.CorrectedQuery.ShouldBeNull("the words of the dropped index are gone");
        result.Hits.ShouldBeEmpty();
    }

    // review of pull request 10, S4: warming up, cancelling
    [Fact]
    public void The_dictionary_can_be_built_ahead_of_the_first_typo_and_the_build_can_be_cancelled()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var words = catalog.Create("words", index => index.SingleWord().TypoTolerance());
        words.Upsert(1, "Apfelkuchen", 10);

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Should.Throw<OperationCanceledException>(() => words.WarmUpTypoDictionary(cancelled.Token));
        Should.Throw<OperationCanceledException>(() => words.RebuildTypoDictionary(cancelled.Token));

        Should.NotThrow(() => words.WarmUpTypoDictionary());
        Should.NotThrow(() => words.WarmUpTypoDictionary());
        words.Query("apflekuchen").TrackTimings().Execute().CorrectedQuery.ShouldBe("apfelkuchen");
    }

    // the shape of the API before 0.1.0 (ADR-0013), the builder and the interfaces
    [Fact]
    public void A_rolled_back_write_leaves_its_beginnings_in_the_typo_dictionary_until_it_is_rebuilt()
    {
        // A documented limit (review of pull request 12): the library does not own the transaction of the
        // application and learns its words when they are written. The rollback itself is correct.
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var titles = catalog.Create("titles", index => index.MultiWord().TypoTolerance());
        titles.Upsert(1, "Schifffahrt", 10);
        titles.WarmUpTypoDictionary();
        titles.Search("schiffb").CorrectedQuery.ShouldBe("schifff");

        using (var connection = database.Connect())
        using (var transaction = connection.BeginTransaction())
        {
            titles.UpsertMany([new LuiEntry(2, "Schiffbruecke")], transaction);
            transaction.Rollback();
        }

        titles.Count().ShouldBe(1);
        var stale = titles.Search("schiffb");
        stale.CorrectedQuery.ShouldBeNull("to the dictionary, schiffb is a beginning of the index now");
        stale.Hits.ShouldBeEmpty();

        titles.RebuildTypoDictionary();

        var rebuilt = titles.Search("schiffb");
        rebuilt.CorrectedQuery.ShouldBe("schifff");
        rebuilt.Hits.ShouldHaveSingleItem().Key.ShouldBe(1);
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

    /// <summary>The rows, with a stop after the first one so that a test can act while the build runs.</summary>
    private static IEnumerable<(string Text, int Weight)> Slow(
        ManualResetEventSlim building,
        ManualResetEventSlim release,
        (string Text, int Weight)[] rows)
    {
        yield return rows[0];
        building.Set();
        release.Wait(TimeSpan.FromSeconds(30));

        foreach (var row in rows.Skip(1))
        {
            yield return row;
        }
    }
}
