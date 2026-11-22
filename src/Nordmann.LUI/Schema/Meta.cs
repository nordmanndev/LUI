namespace Nordmann.LUI.Schema;

/// <summary>
/// The catalog table of the library, <c>lui_meta</c> (ADR-0006, ADR-0033): one row per index. <c>needs_initial_sync</c> is the state
/// of a linked index (ADR-0077): 1 from its creation until its first complete sync, 0 for every embedded index; it is a column and
/// no part of the stored definition, so the definition of an index is the same bytes with or without it.
/// </summary>
internal static class Meta
{
    /// <summary>
    /// The LUI format version — what <c>lui_meta.schema_version</c> holds; not SQLite's own schema counter (<c>PRAGMA
    /// schema_version</c>). Format 1 is the format of 0.3.0, format 2 the format of 0.4.0 (ADR-0073): a file of another format
    /// is rejected when the catalog is opened, with a <see cref="LuiFormatException"/>.
    /// </summary>
    internal const int SchemaVersion = 2;

    internal const string CreateTable =
        """
        CREATE TABLE IF NOT EXISTS lui_meta (
            index_name     TEXT PRIMARY KEY,
            kind           TEXT NOT NULL,
            source         TEXT NOT NULL,
            definition     TEXT NOT NULL,
            schema_version INTEGER NOT NULL,
            created_at     TEXT NOT NULL,
            needs_initial_sync INTEGER NOT NULL DEFAULT 0
        );
        """;
}
