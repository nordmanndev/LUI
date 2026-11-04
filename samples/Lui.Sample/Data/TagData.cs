namespace Nordmann.LUI.Sample.Data;

/// <summary>The rows of the application's own <c>tag</c> table that the sample starts with.</summary>
internal static class TagData
{
    private const string Diet = "Ernährung";
    private const string Kitchen = "Küche";
    private const string Method = "Zubereitung";
    private const string Occasion = "Anlass";

    /// <summary>Name, kind, colour, how often it is used, and its rank (0 = <see cref="LuiPriority.System"/>, 3 = normal).</summary>
    internal static IReadOnlyList<(string Name, string Kind, string Color, int Usage, int Rank)> All { get; } =
    [
        ("vegan", Diet, "#2f855a", 3, 0),          // a system tag: it comes first for every input it matches (E10)
        ("vegetarisch", Diet, "#38a169", 900, 3),
        ("glutenfrei", Diet, "#d69e2e", 410, 3),
        ("laktosefrei", Diet, "#dd6b20", 380, 3),
        ("low carb", Diet, "#3182ce", 330, 3),
        ("eiweißreich", Diet, "#805ad5", 260, 3),
        ("zuckerfrei", Diet, "#d53f8c", 190, 3),
        ("deutsch", Kitchen, "#4a5568", 700, 3),
        ("italienisch", Kitchen, "#c53030", 650, 3),
        ("asiatisch", Kitchen, "#2b6cb0", 430, 3),
        ("mediterran", Kitchen, "#2c7a7b", 360, 3),
        ("orientalisch", Kitchen, "#b7791f", 220, 3),
        ("schwäbisch", Kitchen, "#744210", 90, 3),
        ("schnell", Method, "#319795", 820, 3),
        ("einfach", Method, "#38a169", 780, 3),
        ("Backofen", Method, "#c05621", 540, 3),
        ("Pfanne", Method, "#975a16", 500, 3),
        ("Schongaren", Method, "#6b46c1", 120, 3),
        ("ohne Kochen", Method, "#2f855a", 150, 3),
        ("Weihnachten", Occasion, "#c53030", 330, 3),
        ("Ostern", Occasion, "#d69e2e", 170, 3),
        ("Grillen", Occasion, "#dd6b20", 470, 3),
        ("Kindergeburtstag", Occasion, "#d53f8c", 140, 3),
        ("Silvester", Occasion, "#4c51bf", 80, 3),
        ("#Hashtag-Küche", Occasion, "#718096", 20, 3),
    ];
}
