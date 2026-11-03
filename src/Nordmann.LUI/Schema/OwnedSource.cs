using Nordmann.LUI.Normalization;

namespace Nordmann.LUI.Schema;

/// <summary>
/// The content source <c>Owned</c> (LUI-Spezifikation §5, §7.2, §7.4): the content lives in a table of the
/// library, <c>lui_&lt;name&gt;</c>, with its generated columns, three indexes, an FTS5 table and three
/// triggers; the application writes through the index.
/// </summary>
internal sealed class OwnedSource : ContentSource
{
    /// <summary>The name of the source in <c>lui_meta</c> and in the definition.</summary>
    internal const string SourceName = "Owned";

    internal OwnedSource(string indexName, IndexDefinition definition)
        : base(indexName, definition)
    {
        ContentTable = Content(indexName);
        FtsTable = Fts(indexName);
        CandidateSql =
            $"SELECT f.rowid, c.text FROM {FtsTable} f JOIN {ContentTable} c ON c.doc_id = (f.rowid & {RowId.MaxDocId}) "
            + $"WHERE {FtsTable} MATCH $expr ORDER BY f.rowid LIMIT $n;";
        CountSql = $"SELECT count(*) FROM {ContentTable};";
        TextsSql = $"SELECT text, weight FROM {ContentTable};";
    }

    internal override string ContentTable { get; }

    internal override string FtsTable { get; }

    /// <summary>
    /// The text is fetched by <c>doc_id</c>, the low 32 bits of the rowid: one seek in the table itself, where
    /// a join on <c>s_rowid</c> would first seek in an index (ADR-0006).
    /// </summary>
    internal override string CandidateSql { get; }

    internal override string CountSql { get; }

    internal override string TextsSql { get; }

    // ---- the writes (§12.4) --------------------------------------------------------------------

    internal string Upsert =>
        $"INSERT INTO {ContentTable}(key, text, weight, priority, payload) "
        + "VALUES ($key, $text, $weight, $priority, $payload) "
        + "ON CONFLICT(key) DO UPDATE SET text = excluded.text, weight = excluded.weight, "
        + "priority = excluded.priority, payload = excluded.payload;";

    internal string UpdateWeight => $"UPDATE {ContentTable} SET weight = $weight WHERE key = $key;";

    internal string UpdatePriority => $"UPDATE {ContentTable} SET priority = $priority WHERE key = $key;";

    internal string Remove => $"DELETE FROM {ContentTable} WHERE key = $key;";

    internal override string PrefixSql(int length) =>
        $"SELECT s_rowid, text FROM {ContentTable} WHERE s_p{length} = $q ORDER BY s_rowid LIMIT $n;";

    internal override string HitsSql(string parameters) =>
        $"SELECT doc_id, key, payload FROM {ContentTable} WHERE doc_id IN ({parameters});";

    internal override IReadOnlyList<string> CreateStatements()
    {
        var content = ContentTable;
        var fts = FtsTable;
        var column = Rules.IndexedColumn;

        return
        [
            ContentTableStatement(),
            $"CREATE UNIQUE INDEX {content}_rowid ON {content}(s_rowid);",
            $"CREATE INDEX {content}_p1 ON {content}(s_p1, s_rowid);",
            $"CREATE INDEX {content}_p2 ON {content}(s_p2, s_rowid);",
            $"""
            CREATE VIRTUAL TABLE {fts} USING fts5(
                {column},
                content = '{content}', content_rowid = 's_rowid',
                tokenize = 'trigram remove_diacritics 1');
            """,
            .. Triggers(content, fts, column),
        ];
    }

    /// <summary>The statements that remove an <c>Owned</c> index, in the order they have to run.</summary>
    internal static IReadOnlyList<string> Drop(string indexName)
    {
        var content = Content(indexName);

        return
        [
            $"DROP TRIGGER IF EXISTS {content}_ai;",
            $"DROP TRIGGER IF EXISTS {content}_ad;",
            $"DROP TRIGGER IF EXISTS {content}_au;",
            $"DROP TABLE IF EXISTS {Fts(indexName)};",
            $"DROP TABLE IF EXISTS {content};",
        ];
    }

    private static string Content(string indexName) => $"lui_{indexName}";

    private static string Fts(string indexName) => $"lui_{indexName}_fts";

    /// <summary>
    /// The content table. The prefix columns hold the first letters of the text a query is read against, the
    /// columns of the kind decide which (<see cref="IndexKindRules"/>). lower() folds ASCII only (§6.2).
    /// </summary>
    private string ContentTableStatement() =>
        $"""
        CREATE TABLE {ContentTable} (
            doc_id   INTEGER PRIMARY KEY CHECK (doc_id BETWEEN 0 AND {RowId.MaxDocId}),
            key      TEXT    NOT NULL UNIQUE,
            text     TEXT    NOT NULL,
            weight   INTEGER NOT NULL CHECK (weight BETWEEN 0 AND {RowId.MaxWeight}),
            priority INTEGER NOT NULL DEFAULT 3 CHECK (priority BETWEEN 0 AND 3),
            payload  BLOB,
            s_norm   TEXT GENERATED ALWAYS AS ({SqlNormalizer.Transliteration("text", Definition.GermanTransliteration)}) {Rules.NormStorage}{Rules.CompactColumn},
            s_p1     TEXT GENERATED ALWAYS AS (substr(lower({Rules.PrefixSource}), 1, 1)) STORED,
            s_p2     TEXT GENERATED ALWAYS AS (substr(lower({Rules.PrefixSource}), 1, 2)) STORED,
            s_rowid  INTEGER GENERATED ALWAYS AS ((priority << 61) | (({RowId.MaxWeight} - weight) << 32) | doc_id) STORED
        );
        """;

    /// <summary>The triggers; <paramref name="column"/> is the one column the search index holds.</summary>
    private static string[] Triggers(string content, string fts, string column)
    {
        var added = $"new.{column}";
        var removed = $"old.{column}";

        return
        [
            $"""
            CREATE TRIGGER {content}_ai AFTER INSERT ON {content} BEGIN
                INSERT INTO {fts}(rowid, {column}) VALUES (new.s_rowid, {added});
            END;
            """,
            $"""
            CREATE TRIGGER {content}_ad AFTER DELETE ON {content} BEGIN
                INSERT INTO {fts}({fts}, rowid, {column}) VALUES ('delete', old.s_rowid, {removed});
            END;
            """,

            // Only a change of the rowid or of the indexed text reaches the search index: an identical
            // upsert and a change of the payload alone leave it alone (review P1) — every touch is a
            // delete and an insert, and a new small segment. For a multi-word index only a change of the
            // compact text counts: „Apfel Kuchen" to „Apfel-Kuchen" is none.
            $"""
            CREATE TRIGGER {content}_au AFTER UPDATE OF text, weight, priority ON {content}
            WHEN old.s_rowid IS NOT new.s_rowid OR old.{column} IS NOT new.{column}
            BEGIN
                INSERT INTO {fts}({fts}, rowid, {column}) VALUES ('delete', old.s_rowid, {removed});
                INSERT INTO {fts}(rowid, {column}) VALUES (new.s_rowid, {added});
            END;
            """,
        ];
    }
}
