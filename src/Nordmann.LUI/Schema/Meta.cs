namespace Nordmann.LUI.Schema;

/// <summary>The catalog table of the library, <c>lui_meta</c> (ADR-0006, ADR-0033): one row per index.</summary>
internal static class Meta
{
    /// <summary>The version of the schema an index was created with.</summary>
    internal const int SchemaVersion = 1;

    internal const string CreateTable =
        """
        CREATE TABLE IF NOT EXISTS lui_meta (
            index_name     TEXT PRIMARY KEY,
            kind           TEXT NOT NULL,
            source         TEXT NOT NULL,
            definition     TEXT NOT NULL,
            schema_version INTEGER NOT NULL,
            created_at     TEXT NOT NULL
        );
        """;
}
