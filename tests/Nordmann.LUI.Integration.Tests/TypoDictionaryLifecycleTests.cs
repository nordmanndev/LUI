using Nordmann.LUI.Normalization;
using Shouldly;

namespace Nordmann.LUI.Integration.Tests;

/// <summary>
/// The typo dictionary of an index in its life (#19, ADR-0021): a query does not wait for a build another thread runs —
/// it answers without correction and says so —, a sync marks a built dictionary stale instead of building it anew, and
/// <c>RebuildTypoDictionary</c> brings it in step.
/// </summary>
public sealed class TypoDictionaryLifecycleTests
{
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
        var words = catalog.Index("words").SingleWord().TypoTolerance().Ensure();
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
    public void A_query_whose_build_fails_answers_without_the_correction_and_the_next_one_builds_it()
    {
        // review of pull request 34, I3: a failed build is a correction not searched, not a failed query
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var fail = true;
        catalog.TypoDictionaries.For("words", new NormalizationOptions(), () => fail ? Failing() : [("kartoffel", 1)]);
        var words = catalog.Index("words").SingleWord().TypoTolerance().Ensure();
        words.Upsert(1, "kartoffel");

        var failed = words.Search("kartofel");
        failed.CorrectionSkipped.ShouldBeTrue();
        failed.CorrectedQuery.ShouldBeNull();
        words.TypoDictionaryState.ShouldBe(LuiTypoDictionaryState.NotBuilt);

        fail = false;
        words.Search("kartofel").CorrectedQuery.ShouldBe("kartoffel");
        words.TypoDictionaryState.ShouldBe(LuiTypoDictionaryState.Current);
    }

    [Fact]
    public async Task A_query_that_is_cancelled_while_it_builds_the_dictionary_leaves_it_built()
    {
        // review of pull request 34, I3: the build is the index's, not the query's — an autocomplete cancels at every
        // keystroke, and a build bound to the request would never finish
        using var database = new TestDatabase();
        using var catalog = database.Open();
        using var reading = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var request = new CancellationTokenSource();
        catalog.TypoDictionaries.For("words", new NormalizationOptions(), () => Blocking(reading, release));
        var words = catalog.Index("words").SingleWord().TypoTolerance().Ensure();
        words.Upsert(1, "kartoffel");

        var query = Task.Run(() => words.Search("kartofel", cancellationToken: request.Token));
        reading.Wait(TimeSpan.FromSeconds(10)).ShouldBeTrue("the query builds the dictionary");
        await request.CancelAsync();
        release.Set();

        await Should.ThrowAsync<OperationCanceledException>(query.WaitAsync(TimeSpan.FromSeconds(10)));
        words.TypoDictionaryState.ShouldBe(LuiTypoDictionaryState.Current);
        words.Search("kartofel").CorrectedQuery.ShouldBe("kartoffel");
    }

    [Fact]
    public void A_sync_that_changes_or_removes_marks_the_dictionary_stale_and_a_rebuild_brings_it_in_step()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var words = catalog.Index("words").SingleWord().TypoTolerance().Ensure();
        words.Sync(Entries("kartoffel"));
        words.WarmUpTypoDictionary();
        words.TypoDictionaryState.ShouldBe(LuiTypoDictionaryState.Current);

        words.Sync(Entries("kartoffel", "tomate")).TypoDictionaryStale.ShouldBeFalse();
        words.TypoDictionaryState.ShouldBe(LuiTypoDictionaryState.Current, "a sync that only adds learns the new words");

        words.Sync(Entries("tomate")).TypoDictionaryStale.ShouldBeTrue("the report says it (review of pull request 34, M6)");
        words.TypoDictionaryState.ShouldBe(LuiTypoDictionaryState.Stale);
        words.Search("tomahe").Hits.ShouldHaveSingleItem().Text.ShouldBe("tomate");

        words.RebuildTypoDictionary();
        words.TypoDictionaryState.ShouldBe(LuiTypoDictionaryState.Current);
        words.Search("kartofel").CorrectedQuery.ShouldBeNull();
    }

    [Fact]
    public void A_sync_that_is_cancelled_after_a_change_was_committed_leaves_the_dictionary_stale()
    {
        // review of pull request 34, M4: marked after the commit, not at an end a cancellation never reaches
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var words = catalog.Index("words").SingleWord().TypoTolerance().Ensure();
        words.Sync(Entries("kartoffel", "tomate"));
        words.WarmUpTypoDictionary();
        using var cancellation = new CancellationTokenSource();

        Should.Throw<OperationCanceledException>(() => words.Sync(
            CancelAfterFirst(Entries("gurke", "tomate"), cancellation),
            new LuiSyncOptions { BatchSize = 1 },
            cancellation.Token));

        words.Search("gurke").Hits.ShouldHaveSingleItem("the first batch is committed");
        words.TypoDictionaryState.ShouldBe(LuiTypoDictionaryState.Stale);
    }

    [Fact]
    public void An_index_without_typo_tolerance_has_a_dictionary_that_is_never_built()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var words = catalog.Index("words").SingleWord().Ensure();
        words.Sync(Entries("kartoffel"));

        words.Search("kartofel").CorrectionSkipped.ShouldBeFalse();
        words.TypoDictionaryState.ShouldBe(LuiTypoDictionaryState.NotBuilt);
    }

    private static IEnumerable<LuiEntry<long, LuiNoPayload>> Entries(params string[] texts) =>
        texts.Select((text, i) => new LuiEntry<long, LuiNoPayload>(i + 1, text));

    private static IEnumerable<LuiEntry<long, LuiNoPayload>> CancelAfterFirst(
        IEnumerable<LuiEntry<long, LuiNoPayload>> entries,
        CancellationTokenSource cancellation)
    {
        var first = true;
        foreach (var entry in entries)
        {
            if (!first)
            {
                cancellation.Cancel();
            }

            first = false;
            yield return entry;
        }
    }

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
