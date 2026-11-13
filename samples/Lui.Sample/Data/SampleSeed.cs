using Nordmann.LUI.Sample.Indexes;

namespace Nordmann.LUI.Sample.Data;

/// <summary>
/// The entries the sample starts with, as the sources of the embedded indexes (<c>Sync</c> compares an index with them)
/// and as the first fill of an empty one.
/// </summary>
internal static class SampleSeed
{
    internal static IReadOnlyList<LuiEntry> Recipes() =>
        [.. RecipeData.All.Select((recipe, i) => new LuiEntry(i + 1, recipe.Text)
        {
            Weight = recipe.Weight,
            Priority = recipe.Priority,
            Payload = new RecipePayload(recipe.Category, recipe.Minutes, recipe.Emoji),
        })];

    internal static IReadOnlyList<LuiEntry> Names() =>
        [.. NameData.All.Select((name, i) => new LuiEntry(i + 1, name.Text) { Weight = name.Weight })];

    /// <summary>Fills what is empty, once, and lets the application create the triggers of its linked index — on purpose (ADR-0015).</summary>
    internal static void Apply(SampleIndexes indexes)
    {
        foreach (var index in indexes.All)
        {
            // A bulk index is filled by the button of the index panel, not by the seed (ADR-0029): its source is what the user
            // asks for, and nothing is asked for until then.
            if (index is IBulkSampleIndex)
            {
                continue;
            }

            if (index.Describe().Count == 0)
            {
                index.Sync(CancellationToken.None);
            }

            // The typo tolerance needs its dictionary, and a query never builds it (ADR-0042): the application asks
            // for the build, here — after the entries are there.
            if (!string.Equals(index.Describe().TypoDistance, nameof(LuiTypoDistance.None), StringComparison.Ordinal))
            {
                index.WarmUpTypoDictionary(CancellationToken.None);
            }

            if (index is ILinkedSampleIndex linked)
            {
                linked.SetTriggers(create: true);
            }
        }
    }
}
