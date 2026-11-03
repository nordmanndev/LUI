namespace Nordmann.LUI.Testing;

/// <summary>
/// The stock of recipe titles, used by the tests that ask the multi-word index
/// for recipes. It stood in two files, once with the typo tolerance on and
/// once with it off — one list, two switches.
/// </summary>
public static class RecipeStock
{
    /// <summary>The titles, in the order of the tests that use them.</summary>
    public static readonly (long Key, string Text, int Weight)[] Entries =
    [
        (1, "Apfelkuchen", 900),
        (2, "Gedeckter Apfelkuchen", 800),
        (3, "Apfel Kuchen", 700),
        (4, "Apfelkuchen mit Sahne", 400),
        (5, "Bratapfelkuchen", 9_000),
        (6, "Apfelstrudel", 5_000),
        (7, "Apfelkuchen mit Äpfeln", 300),
        (8, "Hauptstraße", 100)
    ];

    /// <summary>Puts the titles into an index.</summary>
    public static void Fill(ILuiOwnedIndex<long, LuiNoPayload> index)
    {
        foreach (var (key, text, weight) in Entries)
        {
            index.Upsert(key, text, weight);
        }
    }
}
