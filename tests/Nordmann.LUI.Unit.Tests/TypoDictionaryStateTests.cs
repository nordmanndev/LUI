using Nordmann.LUI.Normalization;
using Nordmann.LUI.Typos;
using Shouldly;

namespace Nordmann.LUI.Unit.Tests;

/// <summary>
/// The states of the typo dictionary (ADR-0021): not built, being built, current, stale — and that a query's build does
/// not wait behind another build. The source is a function: no database.
/// </summary>
public sealed class TypoDictionaryStateTests
{
    [Fact]
    public void A_dictionary_goes_from_not_built_to_current_to_stale_and_back()
    {
        var dictionary = new TypoDictionary(new NormalizationOptions(), () => [("kartoffel", 1), ("tomate", 1)]);

        dictionary.State.ShouldBe(LuiTypoDictionaryState.NotBuilt);
        dictionary.MarkStale();
        dictionary.State.ShouldBe(LuiTypoDictionaryState.NotBuilt, "a dictionary that is not built cannot be behind");

        dictionary.EnsureBuilt(CancellationToken.None);
        dictionary.State.ShouldBe(LuiTypoDictionaryState.Current);

        dictionary.MarkStale();
        dictionary.State.ShouldBe(LuiTypoDictionaryState.Stale);

        dictionary.Rebuild(CancellationToken.None);
        dictionary.State.ShouldBe(LuiTypoDictionaryState.Current);
    }

    [Fact]
    public async Task A_build_that_runs_says_so_and_a_second_one_does_not_wait_for_it()
    {
        using var release = new ManualResetEventSlim();
        using var reading = new ManualResetEventSlim();
        var dictionary = new TypoDictionary(new NormalizationOptions(), () => Blocking(reading, release));

        var first = Task.Run(() => dictionary.EnsureBuilt(CancellationToken.None));
        reading.Wait(TimeSpan.FromSeconds(10)).ShouldBeTrue("the first build reads its source");

        dictionary.State.ShouldBe(LuiTypoDictionaryState.Building);
        dictionary.TryEnsureBuilt(CancellationToken.None).ShouldBeFalse("another build runs: no waiting behind it");

        release.Set();
        await first.WaitAsync(TimeSpan.FromSeconds(10));
        dictionary.State.ShouldBe(LuiTypoDictionaryState.Current);
        dictionary.TryEnsureBuilt(CancellationToken.None).ShouldBeTrue();
        dictionary.Corrections("kartofel").ShouldContain(word => word == "kartoffel");
    }

    [Fact]
    public void A_build_that_fails_leaves_the_old_dictionary_as_it_was()
    {
        var fail = false;
        var dictionary = new TypoDictionary(new NormalizationOptions(), () => fail ? Failing() : [("kartoffel", 1)]);
        dictionary.EnsureBuilt(CancellationToken.None);

        fail = true;
        Should.Throw<InvalidOperationException>(() => dictionary.Rebuild(CancellationToken.None));
        dictionary.State.ShouldBe(LuiTypoDictionaryState.Current, "a failed rebuild does not make a current dictionary stale");

        dictionary.MarkStale();
        Should.Throw<InvalidOperationException>(() => dictionary.Rebuild(CancellationToken.None));
        dictionary.State.ShouldBe(LuiTypoDictionaryState.Stale, "nor a stale one current");
        dictionary.Corrections("kartofel").ShouldContain(word => word == "kartoffel", "the old dictionary still answers");
    }

    [Fact]
    public async Task A_sync_while_the_first_build_runs_leaves_the_dictionary_stale()
    {
        // the first build may have read the table before the sync wrote it
        using var release = new ManualResetEventSlim();
        using var reading = new ManualResetEventSlim();
        var dictionary = new TypoDictionary(new NormalizationOptions(), () => Blocking(reading, release));
        var first = Task.Run(() => dictionary.EnsureBuilt(CancellationToken.None));
        reading.Wait(TimeSpan.FromSeconds(10)).ShouldBeTrue();

        dictionary.MarkStale();
        release.Set();
        await first.WaitAsync(TimeSpan.FromSeconds(10));

        dictionary.State.ShouldBe(LuiTypoDictionaryState.Stale);
    }

    private static IEnumerable<(string Text, int Weight)> Blocking(ManualResetEventSlim reading, ManualResetEventSlim release)
    {
        reading.Set();
        release.Wait(TimeSpan.FromSeconds(10));
        yield return ("kartoffel", 1);
    }

    private static IEnumerable<(string Text, int Weight)> Failing()
    {
        yield return ("tomate", 1);
        throw new InvalidOperationException("the table is gone");
    }
}
