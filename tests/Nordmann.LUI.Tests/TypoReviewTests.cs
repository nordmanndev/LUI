using Nordmann.LUI.Normalization;
using Nordmann.LUI.Typos;
using Shouldly;

namespace Nordmann.LUI.Tests;

/// <summary>
/// The findings of the review of pull request 10, as tests: M1 … M3 and S1 were reproduced red before they
/// were fixed.
/// </summary>
public sealed class TypoReviewTests
{
    private static readonly NormalizationOptions Normalization = NormalizationOptions.Default;

    // ---- M1: the build does not hold up the writers, and a rebuild loses nothing ---------------------------

    [Fact]
    public async Task A_write_is_not_held_up_while_the_dictionary_is_being_built()
    {
        using var building = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var dictionary = new TypoDictionary(Normalization, () => Slow(building, release, [("Apfelkuchen", 10)]));

        var build = Task.Run(() => dictionary.EnsureBuilt(CancellationToken.None));
        building.Wait(TimeSpan.FromSeconds(10)).ShouldBeTrue("the build started");

        try
        {
            var add = Task.Run(() => dictionary.Add("Zwetschge", 5));

            (await Task.WhenAny(add, Task.Delay(TimeSpan.FromSeconds(2)))).ShouldBe(add, "a write must not wait for the build");
        }
        finally
        {
            release.Set();
        }

        await build;

        // the word written during the build is in the dictionary afterwards
        dictionary.Corrections("zwetchge").Contains("zwetschge", StringComparer.Ordinal).ShouldBeTrue();
    }

    [Fact]
    public async Task A_word_written_during_a_rebuild_is_not_lost_with_the_swap()
    {
        using var building = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var slow = false;
        var dictionary = new TypoDictionary(
            Normalization,
            () => slow ? Slow(building, release, [("Apfelkuchen", 10)]) : [("Apfelkuchen", 10)]);
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

    [Fact]
    public void A_write_to_a_dictionary_that_is_not_built_is_free_and_forgotten()
    {
        var dictionary = new TypoDictionary(Normalization, () => [("Apfelkuchen", 10)]);

        dictionary.Add("Zwetschge", 5);

        dictionary.IsBuilt.ShouldBeFalse("a write does not build the dictionary");
        dictionary.Corrections("zwetchge").ShouldBeEmpty();
    }

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
            });

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

    // ---- M2: the cap keeps the candidates of the correction ----------------------------------------------

    [Theory]
    [InlineData(50)]
    [InlineData(5)]
    public void A_full_budget_of_hits_in_the_middle_of_words_does_not_push_the_corrected_candidates_out(int candidates)
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var index = catalog.Index("words").SingleWord().Owned<long>().TypoTolerance().Ensure();
        for (var i = 1; i <= 10; i++)
        {
            index.Upsert(i, $"Teigapfle{i:00}", 1_000);
        }

        index.Upsert(100, "Apfelkuchen", 1);

        var result = index.Query("apfle").Candidates(candidates).Execute();

        result.CorrectedQuery.ShouldBe("apfel");
        result.Hits.Select(h => h.Text).Contains("Apfelkuchen", StringComparer.Ordinal).ShouldBeTrue();
        result.Hits[0].Text.ShouldBe("Apfelkuchen", "the word start of the corrected input comes first");
    }

    // ---- M3: DistinctText keeps the correction ----------------------------------------------------------------

    [Fact]
    public void DistinctText_does_not_swallow_the_correction()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var index = catalog.Index("titles").MultiWord().Owned<long>().TypoTolerance().Ensure();
        index.Upsert(1, "Bratapfle Apfel", 10);

        var plain = index.Query("apfle").Execute();
        var distinct = index.Query("apfle").DistinctText().Execute();

        plain.CorrectedQuery.ShouldBe("apfel");
        distinct.CorrectedQuery.ShouldBe("apfel");
        distinct.Hits.ShouldHaveSingleItem().AtWordStart.ShouldBeTrue();
        distinct.Hits[0].Similarity.ShouldBe(plain.Hits[0].Similarity, 1e-9);
    }

    [Fact]
    public void DistinctText_across_the_input_and_its_correction_keeps_one_entry_per_text()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var index = catalog.Index("titles").SingleWord().Owned<long>().TypoTolerance().Ensure();
        index.Upsert(1, "Teigapfle", 10);
        index.Upsert(2, "teigapfle", 20);
        index.Upsert(3, "Apfelkuchen", 5);

        var hits = index.Query("apfle").DistinctText().Execute().Hits;

        hits.Count(h => string.Equals(h.Text, "Teigapfle", StringComparison.OrdinalIgnoreCase)).ShouldBe(1);
        hits.Select(h => h.Text).Contains("Apfelkuchen", StringComparer.Ordinal).ShouldBeTrue();
    }

    // ---- S1: one dictionary per index of a catalog -----------------------------------------------------------

    [Fact]
    public void Two_instances_of_one_index_share_the_dictionary()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var reader = catalog.Index("words").SingleWord().Owned<long>().TypoTolerance().Ensure();
        reader.Upsert(1, "Apfelkuchen", 10);
        reader.Query("apflekuchen").Execute();   // reader builds the dictionary

        var importer = catalog.Index("words").SingleWord().Owned<long>().Ensure();
        importer.Upsert(2, "Zwetschge", 5);

        var result = reader.Query("zwetchge").Execute();
        result.CorrectedQuery.ShouldBe("zwetschge");
        result.Hits.ShouldHaveSingleItem().Key.ShouldBe(2);
    }

    [Fact]
    public void A_dropped_index_does_not_leave_its_words_to_the_next_one_of_that_name()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var first = catalog.Index("words").SingleWord().Owned<long>().TypoTolerance().Ensure();
        first.Upsert(1, "Apfelkuchen", 10);
        first.Query("apflekuchen").Execute().CorrectedQuery.ShouldBe("apfelkuchen");

        catalog.DropIndex("words");
        var second = catalog.Index("words").SingleWord().Owned<long>().TypoTolerance().Ensure();
        second.Upsert(1, "Birnentorte", 10);

        var result = second.Query("apflekuchen").Execute();
        result.CorrectedQuery.ShouldBeNull("the words of the dropped index are gone");
        result.Hits.ShouldBeEmpty();
    }

    // ---- S4: warming up, cancelling ----------------------------------------------------------------------------

    [Fact]
    public void The_dictionary_can_be_built_ahead_of_the_first_typo_and_the_build_can_be_cancelled()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var index = catalog.Index("words").SingleWord().Owned<long>().TypoTolerance().Ensure();
        index.Upsert(1, "Apfelkuchen", 10);

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Should.Throw<OperationCanceledException>(() => index.WarmUpTypoDictionary(cancelled.Token));
        Should.Throw<OperationCanceledException>(() => index.RebuildTypoDictionary(cancelled.Token));

        Should.NotThrow(() => index.WarmUpTypoDictionary());
        Should.NotThrow(() => index.WarmUpTypoDictionary());
        index.Query("apflekuchen").TrackTimings().Execute().CorrectedQuery.ShouldBe("apfelkuchen");
    }

    // ---- N1: the builder takes a bool too ----------------------------------------------------------------------

    [Fact]
    public void The_builder_takes_a_bool_as_well_as_a_number_of_edits()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var on = catalog.Index("a").SingleWord().Owned<long>().TypoTolerance().Ensure();
        var off = catalog.Index("b").SingleWord().Owned<long>().TypoTolerance(true).TypoTolerance(false).Ensure();
        on.Upsert(1, "Apfelkuchen", 1);
        off.Upsert(1, "Apfelkuchen", 1);

        on.Query("apflekuchen").Execute().CorrectedQuery.ShouldBe("apfelkuchen");
        off.Query("apflekuchen").Execute().CorrectedQuery.ShouldBeNull();
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
