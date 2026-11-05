using Bogus;

namespace Nordmann.LUI.Sample.Data;

/// <summary>
/// The <c>people</c> index: the full names of Bogus's German locale. Bogus draws them in the order of a seeded randomizer, so
/// the names are the same in every process — and the first 1,000 names of a fill of 100,000 are the first 1,000 names of it
/// (the source is prefix-stable), which is what makes a fill with more entries a continuation of the one before it.
/// </summary>
internal sealed class PeopleSource : IBulkSource
{
    /// <summary>The seed of the randomizer: the names of the sample are the names of every run.</summary>
    internal const int Seed = 20261105;

    public string Name => "people";

    public LuiIndexKind Kind => LuiIndexKind.MultiWord;

    public bool TypoTolerance => true;

    public string Description =>
        "The full names of Bogus's German locale — the stock of names, with the typo tolerance of the index. " +
        "Nobody knows how “Schröder” is spelled: the dictionary of this index is its own.";

    public string Origin => $"Bogus (locale de, seed {Seed}) — deterministic";

    public long? Capacity => null;

    public Task PrepareAsync(Action<string>? note, CancellationToken cancellationToken) => Task.CompletedTask;

    public IEnumerable<LuiEntry<long, LuiNoPayload>> Entries(int count)
    {
        var faker = new Faker("de") { Random = new Randomizer(Seed) };

        for (var i = 0; i < count; i++)
        {
            var text = faker.Name.FullName();
            yield return new LuiEntry<long, LuiNoPayload>(i + 1, text) { Weight = BulkWeights.Of(text) };
        }
    }
}
