using Nordmann.LUI.Normalization;
using Nordmann.LUI.Typos;
using Shouldly;

namespace Nordmann.LUI.Unit.Tests;

/// <summary>
/// The typo dictionary in its life (ADR-0021, ADR-0025): not built, being built, built — and that a query's build does not
/// wait behind another build, that a built dictionary only grows, and that the dictionary of one index is not the dictionary
/// of another. The source is a function: no database.
/// </summary>
public sealed class TypoDictionaryStateTests
{
    [Fact]
    public void A_dictionary_goes_from_not_built_to_current_and_stays_current_when_it_is_built_anew()
    {
        var dictionary = new TypoDictionary(new NormalizationOptions(), () => [("kartoffel", 1), ("tomate", 1)]);

        dictionary.State.ShouldBe(LuiTypoDictionaryState.NotBuilt);

        dictionary.EnsureBuilt(CancellationToken.None);
        dictionary.State.ShouldBe(LuiTypoDictionaryState.Current);

        dictionary.Rebuild(CancellationToken.None);
        dictionary.State.ShouldBe(LuiTypoDictionaryState.Current);
    }

    [Fact]
    public void A_built_dictionary_learns_a_new_word_at_once_and_a_removed_word_stays_until_it_is_built_anew()
    {
        // ADR-0025: the dictionary only grows. The index is the filter — a correction towards a word that is gone finds no
        // candidate and falls away — so nothing marks the dictionary as behind and nothing rebuilds it on its own.
        var texts = new List<(string Text, int Weight)> { ("kartoffel", 1), ("tomate", 1) };
        var dictionary = new TypoDictionary(new NormalizationOptions(), () => texts.ToArray());
        dictionary.EnsureBuilt(CancellationToken.None);

        dictionary.Add("gurkensalat", 1);
        texts.RemoveAll(entry => string.Equals(entry.Text, "kartoffel", StringComparison.Ordinal));

        // the dictionary holds word beginnings of at most ten letters (ADR-0021): "kartoffel" is one, "gurkensalat" is "gurkensala"
        dictionary.Corrections("gurkensalt").ShouldContain(word => word == "gurkensala", "a new word is known at once");
        dictionary.Corrections("kartofel").ShouldContain(word => word == "kartoffel", "a word that was removed lingers");
        dictionary.State.ShouldBe(LuiTypoDictionaryState.Current, "nothing is stale: there is no such state");

        dictionary.Rebuild(CancellationToken.None);

        dictionary.Corrections("kartofel").ShouldBeEmpty("built anew, the removed word is gone");
        dictionary.Corrections("tomahe").ShouldContain(word => word == "tomate");
    }

    [Fact]
    public void The_dictionaries_of_two_indexes_are_two_dictionaries_and_one_index_has_one()
    {
        // Tags are never corrected towards names, and names never towards tags (owner's requirement, #48): the registry of
        // a catalog is keyed by the name of the index.
        var registry = new TypoDictionaries();
        var normalization = new NormalizationOptions();
        var tags = registry.For("tags", normalization, () => [("vegan", 1), ("glutenfrei", 1)]);
        var names = registry.For("names", normalization, () => [("megan", 1), ("mueller", 1)]);
        tags.EnsureBuilt(CancellationToken.None);
        names.EnsureBuilt(CancellationToken.None);

        tags.Corrections("vegn").ShouldContain(word => word == "vegan");
        names.Corrections("vegn").ShouldNotContain(word => word == "vegan", "the names do not know the word of the tags");
        names.Corrections("megn").ShouldContain(word => word == "megan");
        tags.Corrections("megn").ShouldNotContain(word => word == "megan", "the tags do not know the word of the names");

        registry.For("tags", normalization, () => [("anything", 1)]).ShouldBeSameAs(tags, "two instances of an index share its dictionary");
        registry.Forget("tags");
        registry.For("tags", normalization, () => []).ShouldNotBeSameAs(tags, "a dropped index starts with a dictionary of its own");
    }

    [Fact]
    public async Task A_build_that_runs_says_so_and_the_dictionary_is_not_there_until_it_is_done()
    {
        using var release = new ManualResetEventSlim();
        using var reading = new ManualResetEventSlim();
        var dictionary = new TypoDictionary(new NormalizationOptions(), () => Blocking(reading, release));

        var first = Task.Run(() => dictionary.EnsureBuilt(CancellationToken.None));
        reading.Wait(TimeSpan.FromSeconds(10)).ShouldBeTrue("the first build reads its source");

        dictionary.State.ShouldBe(LuiTypoDictionaryState.Building);
        dictionary.IsBuilt.ShouldBeFalse("the build runs: there is no dictionary yet");

        release.Set();
        await first.WaitAsync(TimeSpan.FromSeconds(10));
        dictionary.State.ShouldBe(LuiTypoDictionaryState.Current);
        dictionary.IsBuilt.ShouldBeTrue();
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

        dictionary.State.ShouldBe(LuiTypoDictionaryState.Current);
        dictionary.Corrections("kartofel").ShouldContain(word => word == "kartoffel", "the old dictionary still answers");
    }

    [Fact]
    public void A_build_that_fails_leaves_no_dictionary_and_the_next_one_tries_again()
    {
        // review of pull request 34, I3: a failed build is a correction not searched, not a failed query. Since the query
        // does not build any more (ADR-0042), the build is asked for — and the failure is where it was asked for.
        var fail = true;
        var dictionary = new TypoDictionary(new NormalizationOptions(), () => fail ? Failing() : [("kartoffel", 1)]);

        Should.Throw<InvalidOperationException>(() => dictionary.EnsureBuilt(CancellationToken.None));
        dictionary.State.ShouldBe(LuiTypoDictionaryState.NotBuilt);

        fail = false;
        dictionary.EnsureBuilt(CancellationToken.None);
        dictionary.State.ShouldBe(LuiTypoDictionaryState.Current);
    }

    [Fact]
    public async Task A_rebuild_that_waits_for_another_build_ends_with_its_token()
    {
        // review of pull request 34, M3: a host that stops is not held up by a build another thread runs
        using var release = new ManualResetEventSlim();
        using var reading = new ManualResetEventSlim();
        using var stopping = new CancellationTokenSource();
        var dictionary = new TypoDictionary(new NormalizationOptions(), () => Blocking(reading, release));
        var first = Task.Run(() => dictionary.EnsureBuilt(CancellationToken.None));
        reading.Wait(TimeSpan.FromSeconds(10)).ShouldBeTrue();

        var waiting = Task.Run(() => dictionary.Rebuild(stopping.Token));
        await stopping.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(waiting.WaitAsync(TimeSpan.FromSeconds(5)));
        release.Set();
        await first.WaitAsync(TimeSpan.FromSeconds(10));
        dictionary.State.ShouldBe(LuiTypoDictionaryState.Current);
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
