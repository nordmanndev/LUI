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
    public void A_query_whose_build_fails_answers_without_the_correction_and_the_next_one_builds_it()
    {
        // review of pull request 34, I3: a failed build is a correction not searched, not a failed query
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var fail = true;
        catalog.TypoDictionaries.For("words", new NormalizationOptions(), () => fail ? Failing() : [("kartoffel", 1)]);
        var words = catalog.Create("words", index => index.SingleWord().TypoTolerance());
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
        var words = catalog.Create("words", index => index.SingleWord().TypoTolerance());
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

    private static IEnumerable<LuiEntry<long, LuiNoPayload>> Entries(params string[] texts) =>
        texts.Select((text, i) => new LuiEntry<long, LuiNoPayload>(i + 1, text));

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
