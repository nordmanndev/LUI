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
    public void A_sync_that_changes_or_removes_marks_the_dictionary_stale_and_a_rebuild_brings_it_in_step()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var words = catalog.Index("words").SingleWord().TypoTolerance().Ensure();
        words.Sync(Entries("kartoffel"));
        words.WarmUpTypoDictionary();
        words.TypoDictionaryState.ShouldBe(LuiTypoDictionaryState.Current);

        words.Sync(Entries("kartoffel", "tomate"));
        words.TypoDictionaryState.ShouldBe(LuiTypoDictionaryState.Current, "a sync that only adds learns the new words");

        words.Sync(Entries("tomate"));
        words.TypoDictionaryState.ShouldBe(LuiTypoDictionaryState.Stale);
        words.Search("tomahe").Hits.ShouldHaveSingleItem().Text.ShouldBe("tomate");

        words.RebuildTypoDictionary();
        words.TypoDictionaryState.ShouldBe(LuiTypoDictionaryState.Current);
        words.Search("kartofel").CorrectedQuery.ShouldBeNull();
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

    private static IEnumerable<(string Text, int Weight)> Blocking(ManualResetEventSlim reading, ManualResetEventSlim release)
    {
        reading.Set();
        release.Wait(TimeSpan.FromSeconds(10));
        yield return ("kartoffel", 1);
    }
}
