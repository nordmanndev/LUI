namespace Nordmann.LUI.Testing;

/// <summary>
/// The table of the application, as a linked index sees it. It was written out
/// in four files, and each of them wrote a slightly different one — the
/// differences were accidental.
/// </summary>
public static class ApplicationTable
{
    /// <summary>The table a linked index works on.</summary>
    public const string Tag = """
        CREATE TABLE tag (
            id          INTEGER PRIMARY KEY,
            name        TEXT,
            usage_count INTEGER NOT NULL DEFAULT 0,
            priority    INTEGER,
            slug        TEXT,
            color       TEXT);
        """;

    /// <summary>
    /// The rows of <see cref="Tag"/>: the names, their weights and — for the
    /// rows that carry one — the columns a payload is read from.
    /// </summary>
    public static readonly (long Id, string? Name, int Weight, string? Slug, string? Color)[] Rows =
    [
        (1, "vegan", 5, "vegan", "green"),
        (2, "vegetarisch", 9, "veggie", "green"),
        (3, "tofu", 7, "tofu", "beige"),
        (4, "veganer", 12, "veganer", "green"),
        (5, "kuchen", 3, "kuchen", "brown")
    ];
}
