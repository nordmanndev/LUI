using Nordmann.LUI.Schema;

namespace Nordmann.LUI.Indexes;

/// <summary>
/// What <c>lui_meta</c> holds about an index (ADR-0006, ADR-0033): its kind, its content source, its definition
/// and the LUI format version it was written with.
/// </summary>
/// <param name="Kind">The kind of the index.</param>
/// <param name="Source">The content source: <c>Embedded</c> or <c>Linked</c>.</param>
/// <param name="Definition">The definition of the index.</param>
/// <param name="SchemaVersion">The LUI format version the index was written with (<c>lui_meta.schema_version</c>, not SQLite's schema counter).</param>
internal sealed record IndexStoreEntry(
    LuiIndexKind Kind,
    string Source,
    IndexDefinition Definition,
    int SchemaVersion);
