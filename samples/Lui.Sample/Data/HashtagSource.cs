using Bogus;

namespace Nordmann.LUI.Sample.Data;

/// <summary>
/// The <c>hashtags</c> index: the tags of Bogus's German locale — a place name and a surname, joined by a hyphen. A tag of one
/// word is findable by its beginning; a tag of two is findable by both of them, because the index of a single word searches the
/// text as a whole and its trigrams with it.
/// </summary>
internal sealed class HashtagSource : IBulkSource
{
    /// <summary>The seed of the randomizer: the tags of the sample are the tags of every run.</summary>
    internal const int Seed = 20261105;

    public string Name => "hashtags";

    public LuiIndexKind Kind => LuiIndexKind.SingleWord;

    public bool TypoTolerance => false;

    public string Description =>
        "The tags of Bogus's German locale: a place and a surname, joined by a hyphen — “#Cedrikdorf-Gehrke”. " +
        "A tag of two words is found by either of them.";

    public string Origin => $"Bogus (locale de, seed {Seed}) — deterministic";

    public long? Capacity => null;

    public Task PrepareAsync(Action<string>? note, CancellationToken cancellationToken) => Task.CompletedTask;

    public IEnumerable<LuiEntry<long, LuiNoPayload>> Entries(int count)
    {
        var faker = new Faker("de") { Random = new Randomizer(Seed) };

        for (var i = 0; i < count; i++)
        {
            // The place name of the locale is written as one word: a tag is one token ("Neustadt", not "Neu Stadt").
            var place = faker.Address.City().Replace(" ", string.Empty, StringComparison.Ordinal);
            var text = $"#{place}-{faker.Name.LastName()}";
            yield return new LuiEntry<long, LuiNoPayload>(i + 1, text) { Weight = BulkWeights.Of(text) };
        }
    }
}
