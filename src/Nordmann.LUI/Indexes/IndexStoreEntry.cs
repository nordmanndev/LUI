using Nordmann.LUI.Schema;

namespace Nordmann.LUI.Indexes;

/// <summary>
/// What <c>lui_meta</c> holds about an index (ADR-0006, ADR-0033): its kind, its content source, its definition
/// and the version of the schema it was written with.
/// </summary>
/// <param name="Kind">The kind of the index.</param>
/// <param name="Source">The content source: <c>Embedded</c> or <c>Linked</c>.</param>
/// <param name="Definition">The definition of the index.</param>
/// <param name="SchemaVersion">The version of the schema the index was written with.</param>
internal sealed record IndexStoreEntry(
    LuiIndexKind Kind,
    string Source,
    IndexDefinition Definition,
    int SchemaVersion);
