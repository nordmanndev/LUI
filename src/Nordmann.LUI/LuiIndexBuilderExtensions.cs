using Nordmann.LUI.Schema;

namespace Nordmann.LUI;

/// <summary>The step from the default, an owned index, to an index over a table of the application.</summary>
public static class LuiIndexBuilderExtensions
{
    /// <summary>
    /// Links the index to a table of the application instead (<c>Linked</c>, ADR-0015): it searches the table's
    /// <paramref name="text"/>, its key is the table's <c>INTEGER PRIMARY KEY</c>. LUI does not change the table —
    /// it reads it, and the application keeps the index current with <c>Sync</c> and <c>Refresh</c>, or with triggers
    /// it creates on purpose. Offered only before a key or a payload is chosen: a linked index has the key of its table
    /// and reads its payload from its columns.
    /// </summary>
    /// <param name="builder">The builder of the index.</param>
    /// <param name="table">The table of the application.</param>
    /// <param name="key">Its INTEGER PRIMARY KEY.</param>
    /// <param name="text">The column that is searched and shown.</param>
    /// <param name="weight">The column of the popularity (0 … 536,870,911); <see langword="null"/> for none.</param>
    /// <param name="priority">
    /// The column of the priority, with the values of <see cref="LuiPriority"/> — 0 is <see cref="LuiPriority.System"/>,
    /// 3 is <see cref="LuiPriority.Normal"/>, a smaller number ranks first; another value counts as normal.
    /// <see langword="null"/> for none: every entry is normal.
    /// </param>
    /// <returns>The builder of the linked index, with the settings made so far.</returns>
    /// <exception cref="ArgumentException">A name is not a name of the pattern <c>^[A-Za-z_][A-Za-z0-9_]{0,63}$</c>.</exception>
    public static LuiLinkedIndexBuilder<LuiNoPayload> LinkTo(
        this LuiIndexBuilder<long, LuiNoPayload> builder,
        string table,
        string key,
        string text,
        string? weight = null,
        string? priority = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var link = new LinkDefinition(
            Identifier.ValidateApplicationName(table, nameof(table)),
            Identifier.ValidateApplicationName(key, nameof(key)),
            Identifier.ValidateApplicationName(text, nameof(text)),
            weight is null ? null : Identifier.ValidateApplicationName(weight, nameof(weight)),
            priority is null ? null : Identifier.ValidateApplicationName(priority, nameof(priority)));

        return new LuiLinkedIndexBuilder<LuiNoPayload>(builder.Catalog, builder.Name, builder.Kind, link, [], null)
            .WithSettings(builder.SimilaritySetting, builder.GermanTransliterationSetting, builder.TypoToleranceSetting);
    }
}
