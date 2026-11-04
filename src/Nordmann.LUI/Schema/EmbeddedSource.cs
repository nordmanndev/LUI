using Nordmann.LUI.Normalization;

namespace Nordmann.LUI.Schema;

/// <summary>
/// The content source <c>Embedded</c> (LUI-Spezifikation §5, §7.2, §7.4): the content lives in a table of the
/// library, <c>lui_&lt;name&gt;</c>, with its generated columns, three indexes, an FTS5 table and three
/// triggers; the application writes through the index.
/// </summary>
internal sealed class EmbeddedSource : ContentSource
{
    /// <summary>The name of the source in <c>lui_meta</c> and in the definition.</summary>
    internal const string SourceName = "Embedded";

    internal EmbeddedSource(string indexName, IndexDefinition definition)
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

    // ---- the sync and the check (§12.4, §12.6, §14) -------------------------------------------------

    /// <summary>
    /// The stored entry of <c>$key</c>: <c>(doc_id, same)</c>, <c>same</c> being 1 when text, weight, priority and payload
    /// are those of the parameters — a sync writes only what differs, so a sync that runs again after an abort skips what
    /// the first one committed.
    /// </summary>
    internal string CompareSql =>
        $"SELECT doc_id, text IS $text AND weight IS $weight AND priority IS $priority AND payload IS $payload "
        + $"FROM {ContentTable} WHERE key = $key;";

    /// <summary>
    /// The table of a sync, on the writer's connection: the keys that were in the index when the sync began, and that it
    /// has not found in the source yet. What is left at the end is removed — by key, so the outcome does not depend on
    /// the document ids: a key the application adds meanwhile stays, a key that was there and is not in the source goes.
    /// </summary>
    internal static string CreateStartSql(string start) => $"CREATE TEMP TABLE {start} (key TEXT PRIMARY KEY);";

    /// <summary>Notes every key of the index in the table of a sync.</summary>
    internal string SnapshotSql(string start) => $"INSERT INTO {start}(key) SELECT key FROM {ContentTable};";

    /// <summary>Crosses the key <c>$key</c> off the table of a sync: the source has it.</summary>
    internal static string FoundInSourceSql(string start) => $"DELETE FROM {start} WHERE key = $key;";

    internal static string DropTempSql(string table) => $"DROP TABLE IF EXISTS temp.{table};";

    /// <summary>
    /// The next <c>$n</c> keys the source did not have, after the row <c>$after</c> of the table: <c>(rowid, key)</c> — by
    /// the rowid of the table, not by the key, so that the empty text key is not passed over.
    /// </summary>
    internal static string NotFoundSql(string start) => $"SELECT rowid, key FROM {start} WHERE rowid > $after ORDER BY rowid LIMIT $n;";

    /// <summary>Removes the entry of the key <c>$key</c>, if it is still there.</summary>
    internal string RemoveNotFoundSql => $"DELETE FROM {ContentTable} WHERE key = $key;";

    /// <summary>The entries whose key a check did not find in the source: their count, or the keys of the first <c>$n</c>.</summary>
    internal string NotInSourceSql(string found, bool count) =>
        count
            ? $"SELECT count(*) FROM {ContentTable} WHERE key NOT IN (SELECT key FROM {found});"
            : $"SELECT key FROM {ContentTable} WHERE key NOT IN (SELECT key FROM {found}) ORDER BY doc_id LIMIT $n;";

    /// <summary>A key of the source that is not in the index.</summary>
    internal const int FoundMissing = 0;

    /// <summary>A key of the source whose entry is as in the source.</summary>
    internal const int FoundSame = 1;

    /// <summary>A key of the source whose entry differs.</summary>
    internal const int FoundDifferent = 2;

    /// <summary>The table of a check: every key of the source, with what it found (<see cref="FoundMissing"/> …).</summary>
    internal static string CreateFoundSql(string found) => $"CREATE TEMP TABLE {found} (key TEXT PRIMARY KEY, state INTEGER NOT NULL);";

    /// <summary>Notes what <c>$key</c> found; a later entry of the same key replaces it.</summary>
    internal static string FoundSql(string found) => $"INSERT OR REPLACE INTO {found}(key, state) VALUES ($key, $state);";

    /// <summary>The first <c>$n</c> keys with a state, each with the count of all of them: <c>(key, count)</c>.</summary>
    internal static string FoundKeysSql(string found, int state) =>
        $"SELECT key, count(*) OVER () FROM {found} WHERE state = {state.ToString(System.Globalization.CultureInfo.InvariantCulture)} ORDER BY key LIMIT $n;";

    /// <summary>Builds the search index anew from the content table — FTS5 reads it in the order of the rowid.</summary>
    internal string RebuildSql => $"INSERT INTO {FtsTable}({FtsTable}) VALUES ('rebuild');";

    /// <summary>
    /// The integrity check of the search index <b>against the content table</b> (<c>rank = 1</c>): a row the triggers
    /// did not carry into the search index is found too.
    /// </summary>
    internal override string IntegrityCheckSql => $"INSERT INTO {FtsTable}({FtsTable}, rank) VALUES ('integrity-check', 1);";

    /// <summary>The three triggers that keep the search index in step: <c>(name, sql)</c> as SQLite stores them.</summary>
    internal IReadOnlyList<(string Name, string Sql)> TriggerStatements()
    {
        var content = ContentTable;
        return
        [
            .. Triggers(content, FtsTable, Rules.IndexedColumn)
                .Select(sql => (Name: sql.Split(' ', 4)[2], Sql: sql.Trim().TrimEnd(';'))),
        ];
    }

    /// <summary>The statements that read the triggers as SQLite stores them: <c>(name, sql)</c>.</summary>
    internal string StoredTriggersSql =>
        $"SELECT name, sql FROM sqlite_master WHERE type = 'trigger' AND tbl_name = '{ContentTable}' "
        + $"AND name IN ('{ContentTable}_ai', '{ContentTable}_ad', '{ContentTable}_au');";

    /// <summary>Removes the three triggers, if they are there.</summary>
    internal string DropTriggersSql =>
        $"DROP TRIGGER IF EXISTS {ContentTable}_ai; DROP TRIGGER IF EXISTS {ContentTable}_ad; DROP TRIGGER IF EXISTS {ContentTable}_au;";

    internal override string PrefixSql(int length) =>
        $"SELECT s_rowid, text FROM {ContentTable} WHERE s_p{length} = $q ORDER BY s_rowid LIMIT $n;";

    internal override string HitsSql(string parameters, IReadOnlyList<string> payloadColumns) =>
        $"SELECT doc_id, key{string.Concat(payloadColumns.Select(column => ", " + column))} FROM {ContentTable} WHERE doc_id IN ({parameters});";

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

    /// <summary>The statements that remove an <c>Embedded</c> index, in the order they have to run.</summary>
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
            s_norm   TEXT GENERATED ALWAYS AS ({SqlNormalizer.Transliteration("text", Definition.GermanTransliteration)}) {Rules.NormStorage}{CompactColumn(Rules)},
            s_p1     TEXT GENERATED ALWAYS AS (substr(lower({Rules.PrefixSource}), 1, 1)) STORED,
            s_p2     TEXT GENERATED ALWAYS AS (substr(lower({Rules.PrefixSource}), 1, 2)) STORED,
            s_rowid  INTEGER GENERATED ALWAYS AS ((priority << 61) | (({RowId.MaxWeight} - weight) << 32) | doc_id) STORED
        );
        """;

    /// <summary>The compact column of the kind after <c>s_norm</c>, in the layout of the content table.</summary>
    private static string CompactColumn(IndexKindRules rules) =>
        rules.CompactColumnDefinition is { } definition ? $",\n    {definition}" : string.Empty;

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
