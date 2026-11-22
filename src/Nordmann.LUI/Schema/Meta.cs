namespace Nordmann.LUI.Schema;

/// <summary>
/// The catalog table of the library, <c>lui_meta</c> (ADR-0006, ADR-0033): one row per index. <c>needs_initial_sync</c> is the state
/// of a linked index (ADR-0077): 1 from its creation until its first complete sync, 0 for every embedded index; it is a column and
/// no part of the stored definition, so the definition of an index is the same bytes with or without it. So are the columns of the
/// sync of a linked index (ADR-0077): <c>sync_generation</c>, the number of the last generation a sync took, and the lease that
/// lets one sync of an index run at a time — <c>sync_lease_owner</c> and <c>sync_lease_expires</c> (UTC, in milliseconds since
/// 1970). They are NULL or 0 for an embedded index and for a linked index that is not syncing.
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
            needs_initial_sync INTEGER NOT NULL DEFAULT 0,
            sync_generation    INTEGER NOT NULL DEFAULT 0,
            sync_lease_owner   TEXT,
            sync_lease_expires INTEGER
        );
        """;

    /// <summary>
    /// The columns that format 2 gained after 0.3.0, in the order of <see cref="CreateTable"/>, with what <c>ALTER TABLE … ADD COLUMN</c>
    /// declares. A <c>lui_meta</c> that has rows and misses one of them was written by an early build of 0.4.0 and is refused when the
    /// catalog opens; one without a row is the empty table of 0.3.0, and the first <c>Create</c> adds them (ADR-0077).
    /// </summary>
    internal static readonly (string Name, string Declaration)[] StateColumns =
    [
        ("needs_initial_sync", "INTEGER NOT NULL DEFAULT 0"),
        ("sync_generation", "INTEGER NOT NULL DEFAULT 0"),
        ("sync_lease_owner", "TEXT"),
        ("sync_lease_expires", "INTEGER"),
    ];
}
