namespace Nordmann.LUI;

/// <summary>
/// What a catalog says about one of its indexes, by name (ADR-0076): what <see cref="LuiCatalog.Indexes"/> returns for a tool, an
/// admin endpoint or a health check that knows neither the definitions of the application nor its payload types. The definition
/// fields are what <c>lui_meta</c> stores and the catalog holds in memory (ADR-0075); the one state field, the typo dictionary, is
/// read when <c>Indexes()</c> is called. It is a snapshot: it does not change when the index does. It is no handle — nothing can
/// be searched or written through it.
/// </summary>
public sealed record LuiIndexInfo
{
    /// <summary>The name of the index.</summary>
    public required string Name { get; init; }

    /// <summary>Where the content lives: in a table of the library (<see cref="LuiIndexSource.Embedded"/>) or in a table of the application (<see cref="LuiIndexSource.Linked"/>). It is called <c>Source</c> and not <c>Kind</c> because <see cref="Kind"/> already means single word or multi word.</summary>
    public required LuiIndexSource Source { get; init; }

    /// <summary>Single word or multi word — how the index is searched.</summary>
    public required LuiIndexKind Kind { get; init; }

    /// <summary>
    /// The stable name of the payload type of an embedded index that was created with one (ADR-0072); <see langword="null"/> for an
    /// index without a declared payload type, and for every linked index — its payload is built from the columns of its table by code
    /// of the application, and nothing of it is stored.
    /// </summary>
    public string? PayloadType { get; init; }

    /// <summary>The format of the serializer that wrote the payloads of an embedded index (ADR-0072); <see langword="null"/> exactly when <see cref="PayloadType"/> is.</summary>
    public string? PayloadFormat { get; init; }

    /// <summary>The type of the keys, as the stored definition names it (ADR-0073).</summary>
    public required LuiKeyType KeyType { get; init; }

    /// <summary>How many errors the index forgives: the distance of the stored definition.</summary>
    public required LuiTypoDistance TypoDistance { get; init; }

    /// <summary>
    /// Where the typo dictionary of the index stands in this catalog (ADR-0012, ADR-0025) when <c>Indexes()</c> was called: the dictionary
    /// lives in the memory of the process, one per index and definition, so <c>NotBuilt</c> says that this catalog has not built it.
    /// </summary>
    public required LuiTypoDictionaryState TypoDictionaryState { get; init; }
}
