using Nordmann.LUI.Normalization;

namespace Nordmann.LUI.Schema;

/// <summary>
/// The content source <c>Linked</c> (ADR-0014, ADR-0015, ADR-0015, ADR-0015): the content lives in a table of the
/// application, and LUI never changes that table on its own — no column, no index, no trigger unless the
/// application asks for one. LUI keeps two tables of its own: <c>lui_&lt;name&gt;</c> without text (the document
/// id, the packed rowid, the two prefix keys and the generation of the sync that wrote the row) and a <b>contentless</b> FTS5 table over the search form. The text
/// and the payload are read from the application's table by its primary key, so the text is not stored twice.
/// <para>
/// The forms are computed by the same SQL expressions that generate the columns of <c>Embedded</c>, inside the
/// statements that write the index: <see cref="RefreshSql"/> for one row, <see cref="BatchEntriesSql"/> and <see cref="BatchSearchSql"/>
/// for a batch of rows — and, if the application wants them, the same statements in three triggers (<see cref="TriggerScript"/>).
/// Every name of the application is checked against <see cref="Identifier.ApplicationPattern"/> and quoted.
/// </para>
/// </summary>
internal sealed class LinkedSource : ContentSource
{
    /// <summary>The name of the source in <c>lui_meta</c> and in the definition.</summary>
    internal const string SourceName = "Linked";

    private readonly string _key;
    private readonly string _text;
    private readonly string _weight;
    private readonly string _priority;
    private readonly string _norm;
    private readonly IReadOnlyList<string> _payloadColumns;

    internal LinkedSource(string indexName, IndexDefinition definition)
        : base(indexName, definition)
    {
        Link = definition.Link
            ?? throw new ArgumentException("A linked index needs the table it is linked to.", nameof(definition));

        // the key of a linked index is the document id: 0 … 2³²−1, and for an int index 0 … 2³¹−1 (ADR-0073)
        MaxKey = KeyCodec.TypeOf(definition.KeyType) == LuiKeyType.Int ? int.MaxValue : RowId.MaxDocId;

        // the columns of the payload come out of the definition: they reach SQL, so they are checked again
        _payloadColumns = Link.PayloadColumns is { } columns
            ? [.. columns.Select(column => Identifier.ValidateApplicationName(column, "the payload columns of the definition"))]
            : [];

        ContentTable = Identifier.Quote(Link.Table);
        FtsTable = Fts(indexName);
        EntryTable = Entries(indexName);
        PendingTable = Pending(indexName);
        _key = $"h.{Identifier.Quote(Link.Key)}";
        _text = $"h.{Identifier.Quote(Link.Text)}";

        // A weight or a priority out of range would break the packed rowid; the application's table may hold
        // anything, so they are brought into range: a negative weight is 0, a priority outside 0 … 3 is normal.
        _weight = Link.Weight is { } weight
            ? $"min(max(coalesce(CAST(h.{Identifier.Quote(weight)} AS INTEGER), 0), 0), {RowId.MaxWeight})"
            : "0";
        _priority = Link.Priority is { } priority
            ? $"CASE WHEN CAST(h.{Identifier.Quote(priority)} AS INTEGER) BETWEEN 0 AND 3 THEN CAST(h.{Identifier.Quote(priority)} AS INTEGER) ELSE 3 END"
            : "3";
        _norm = SqlNormalizer.Transliteration(_text, definition.GermanTransliteration);

        CandidateSql =
            $"SELECT f.rowid, {_text} FROM {FtsTable} f JOIN {ContentTable} h ON {_key} = (f.rowid & {RowId.MaxDocId}) "
            + $"WHERE {FtsTable} MATCH $expr ORDER BY f.rowid LIMIT $n;";
        CountSql = $"SELECT count(*) FROM {EntryTable};";
        TextsSql = $"SELECT {_text}, {_weight} FROM {EntryTable} s JOIN {ContentTable} h ON {_key} = s.doc_id;";
    }

    internal override bool TextCanChangeUnderTheIndex => true;

    /// <summary>The table of the application and its columns.</summary>
    internal LinkDefinition Link { get; }

    /// <summary>
    /// The largest key a row may have to be indexed: the largest document id, <c>2³²−1</c> — and for an <c>int</c> index
    /// <see cref="int.MaxValue"/>. A row with another key (below 0, or above this) is left out and counted in
    /// <c>KeyOutOfRange</c>; it never reaches a query.
    /// </summary>
    internal long MaxKey { get; }

    /// <summary>The quoted table of the application.</summary>
    internal override string ContentTable { get; }

    internal override string FtsTable { get; }

    /// <summary>
    /// The table of LUI: <c>(doc_id, s_rowid, s_p1, s_p2, gen)</c>, no text. <c>gen</c> is the generation of the sync that wrote the
    /// row (ADR-0077): a sync takes the next number, every batch writes it, and what still carries an older one when the batches are
    /// done is not in the table of the application any more.
    /// </summary>
    internal string EntryTable { get; }

    /// <summary>
    /// The outbox of the triggers: the keys of the rows that changed since the index was last brought in step —
    /// <c>(doc_id)</c>, plain SQL, no check, so that any SQLite can write it (review of pull request 16, M1).
    /// </summary>
    internal string PendingTable { get; }

    /// <summary>The next keys of the outbox, the smallest first, at most <c>$n</c>.</summary>
    internal string PendingBatchSql => $"SELECT doc_id FROM {PendingTable} ORDER BY doc_id LIMIT $n;";

    /// <summary>Takes <c>$id</c> out of the outbox: its entry is in step with its row.</summary>
    internal string ForgetPendingSql => $"DELETE FROM {PendingTable} WHERE doc_id = $id;";

    /// <summary>The number of keys in the outbox.</summary>
    internal string PendingCountSql => $"SELECT count(*) FROM {PendingTable};";

    internal override string CandidateSql { get; }

    internal override string CountSql { get; }

    /// <summary>The texts of the entries that are in the index — not of every row of the table.</summary>
    internal override string TextsSql { get; }

    /// <summary>The text and the weight of one row, if it would be indexed: <c>(text, weight)</c> for <c>$id</c>.</summary>
    internal string ReadRowSql => $"SELECT {_text}, {_weight} FROM {ContentTable} h WHERE {_key} = $id AND {Indexable};";

    // ---- the sync in batches (ADR-0077) ----------------------------------------------------------------------------------------

    /// <summary>
    /// The keys that end the next batch of a sync and begin the one after it: the <c>$size</c>-th key after <c>$from</c> and the one that
    /// follows it — none, when the batch is the last one, and one, when the batch is exactly the rest of the table.
    /// </summary>
    internal string ChunkEndSql =>
        $"SELECT {_key} FROM {ContentTable} h WHERE {_key} > $from ORDER BY {_key} LIMIT 2 OFFSET $size - 1;";

    /// <summary>
    /// What the rows of the batch <c>($from, $to]</c> are: <c>(rows read, rows without a text, rows with a text and a key out of range)</c>.
    /// The rows that are left out are counted where they are read — the batches add up to what the table has, without a second pass over it.
    /// </summary>
    internal string BatchRowsSql =>
        $"SELECT count(*), coalesce(sum(CASE WHEN NOT ({HasText}) THEN 1 ELSE 0 END), 0), "
        + $"coalesce(sum(CASE WHEN ({HasText}) AND NOT ({KeyInRange}) THEN 1 ELSE 0 END), 0) FROM {ContentTable} h "
        + $"WHERE {_key} > $from AND {_key} <= $to;";

    /// <summary>
    /// First statement of a batch: the search entries of the rows of the batch that exist already — and will be written again — leave the
    /// search index. An entry whose row is not indexable any more stays until the removal at the end: nothing of the previous state goes
    /// before the new one is complete.
    /// </summary>
    internal string BatchDropReplacedSql =>
        $"DELETE FROM {FtsTable} WHERE rowid IN (SELECT s.s_rowid FROM {EntryTable} s JOIN {ContentTable} h ON {_key} = s.doc_id "
        + $"WHERE s.doc_id > $from AND s.doc_id <= $to AND {Indexable});";

    /// <summary>
    /// Second statement of a batch: the entries of the indexable rows of <c>($from, $to]</c>, added or replaced by key, with the
    /// generation <c>$gen</c> of the sync.
    /// </summary>
    internal string BatchEntriesSql
    {
        get
        {
            var prefix = $"lower({Rules.PrefixValue(_norm)})";
            return
                $"INSERT INTO {EntryTable}(doc_id, s_rowid, s_p1, s_p2, gen) "
                + $"SELECT {_key}, {RowIdValue}, substr({prefix}, 1, 1), substr({prefix}, 1, 2), $gen "
                + $"FROM {ContentTable} h WHERE {_key} > $from AND {_key} <= $to AND {Indexable} "
                + "ON CONFLICT(doc_id) DO UPDATE SET s_rowid = excluded.s_rowid, s_p1 = excluded.s_p1, s_p2 = excluded.s_p2, gen = excluded.gen;";
        }
    }

    /// <summary>Third statement of a batch: the search index, in the order of the rowid, for the entries the batch wrote.</summary>
    internal string BatchSearchSql =>
        $"INSERT INTO {FtsTable}(rowid, {Rules.IndexedColumn}) "
        + $"SELECT s.s_rowid, {Rules.IndexedValue(_norm)} FROM {EntryTable} s JOIN {ContentTable} h ON {_key} = s.doc_id "
        + $"WHERE s.doc_id > $from AND s.doc_id <= $to AND s.gen = $gen AND {Indexable} ORDER BY s.s_rowid;";

    /// <summary>
    /// Last statement of a batch: the keys of the outbox that the batch brought in step leave it — their rows are current now. A key whose
    /// row is gone, or not indexable, stays for <c>ApplyPending</c>.
    /// </summary>
    internal string BatchForgetPendingSql =>
        $"DELETE FROM {PendingTable} WHERE doc_id > $from AND doc_id <= $to "
        + $"AND doc_id IN (SELECT doc_id FROM {EntryTable} WHERE doc_id > $from AND doc_id <= $to AND gen = $gen);";

    /// <summary>
    /// The entries of an earlier generation, the oldest first, at most <c>$n</c>: <c>(doc_id, s_rowid)</c>. Generations only grow, and the
    /// lease holds that no other sync runs, so "another generation than the current one" is "an older one" — which the index on
    /// <c>gen</c> reads as a range, where <c>&lt;&gt;</c> would read the whole table for every batch of the removal.
    /// </summary>
    internal string StaleEntriesSql =>
        $"SELECT doc_id, s_rowid FROM {EntryTable} WHERE gen < $gen ORDER BY gen, doc_id LIMIT $n;";

    /// <summary>Removes the entry of <c>$id</c> and its search entry — one entry of the previous generation.</summary>
    internal string RemoveStaleSql => RemoveSql("$id");

    // ---- the merge in steps (ADR-0077): FTS5 'merge', N pages at a time ---------------------------------------------------------

    /// <summary>
    /// What a step of the merge reports with: SQLite's <c>total_changes()</c>, read before and after <see cref="MergeSql"/> on the
    /// connection of the step. A step that merged something wrote into the shadow tables of FTS5, and the count moves by more than
    /// the one change of the <c>INSERT</c> itself; a step that found nothing left moves it by exactly that one. <c>changes()</c>
    /// says 1 after every step and is not the signal (<c>LinkedMergeStepTests</c>).
    /// </summary>
    internal const string TotalChangesSql = "SELECT total_changes();";

    /// <summary>The step of the incremental merge of a search index: at most <c>$pages</c> pages of FTS5's segments are merged.</summary>
    /// <param name="ftsTable">The (quoted) search index.</param>
    /// <returns>The statement, with the parameter <c>$pages</c>.</returns>
    internal static string MergeSql(string ftsTable) => $"INSERT INTO {ftsTable}({ftsTable}, rank) VALUES ('merge', $pages);";

    /// <summary><see cref="MergeSql(string)"/> of this index.</summary>
    internal string MergeStepSql => MergeSql(FtsTable);

    // ---- the check (ADR-0016, ADR-0016, ADR-0046): the entries against the table -------------------------------------

    /// <summary>The rows of the table the index should hold and does not: their count, or their first <c>$n</c> keys.</summary>
    internal string MissingSql(bool count) =>
        $"SELECT {(count ? "count(*)" : _key)} FROM {ContentTable} h WHERE {Indexable} "
        + $"AND NOT EXISTS (SELECT 1 FROM {EntryTable} s WHERE s.doc_id = {_key}){(count ? ";" : $" ORDER BY {_key} LIMIT $n;")}";

    /// <summary>The entries whose row is gone or would not be indexed any more.</summary>
    internal string ExtraSql(bool count) =>
        $"SELECT {(count ? "count(*)" : "s.doc_id")} FROM {EntryTable} s WHERE NOT EXISTS "
        + $"(SELECT 1 FROM {ContentTable} h WHERE {_key} = s.doc_id AND {Indexable}){(count ? ";" : " ORDER BY s.doc_id LIMIT $n;")}";

    /// <summary>
    /// The entries whose row has another weight, priority or beginning now: the stored rowid and prefix keys against the
    /// ones the row gives. A change of the text behind its first two letters is not seen here — a query never shows such
    /// a text (it is checked against the row), and <c>Sync()</c> repairs it.
    /// </summary>
    internal string DifferentSql(bool count)
    {
        var prefix = $"lower({Rules.PrefixValue(_norm)})";
        return
            $"SELECT {(count ? "count(*)" : "s.doc_id")} FROM {EntryTable} s JOIN {ContentTable} h ON {_key} = s.doc_id "
            + $"WHERE {Indexable} AND (s.s_rowid IS NOT {RowIdValue} OR s.s_p1 IS NOT substr({prefix}, 1, 1) "
            + $"OR s.s_p2 IS NOT substr({prefix}, 1, 2)){(count ? ";" : " ORDER BY s.doc_id LIMIT $n;")}";
    }

    internal override string PrefixSql(int length) =>
        $"SELECT s.s_rowid, {_text} FROM {EntryTable} s JOIN {ContentTable} h ON {_key} = s.doc_id "
        + $"WHERE s.s_p{length} = $q ORDER BY s.s_rowid LIMIT $n;";

    internal override string HitsSql(string parameters, IReadOnlyList<string> payloadColumns) =>
        $"SELECT {_key}, {_key}{string.Concat(payloadColumns.Select(column => $", h.{Identifier.Quote(column)}"))} "
        + $"FROM {ContentTable} h WHERE {_key} IN ({parameters});";

    /// <summary>
    /// The columns of the payload, as the definition holds them — the columns the mapping of the application reads
    /// (ADR-0033).
    /// </summary>
    internal IReadOnlyList<string> PayloadColumns => _payloadColumns;

    internal override IReadOnlyList<string> CreateStatements() =>
    [
        $"""
        CREATE TABLE {EntryTable} (
            doc_id  INTEGER PRIMARY KEY CHECK (doc_id BETWEEN 0 AND {RowId.MaxDocId}),
            s_rowid INTEGER NOT NULL UNIQUE,
            s_p1    TEXT    NOT NULL,
            s_p2    TEXT    NOT NULL,
            gen     INTEGER NOT NULL
        );
        """,
        $"CREATE INDEX {EntryTable}_p1 ON {EntryTable}(s_p1, s_rowid);",
        $"CREATE INDEX {EntryTable}_p2 ON {EntryTable}(s_p2, s_rowid);",
        $"CREATE INDEX {EntryTable}_gen ON {EntryTable}(gen);",
        $"CREATE TABLE {PendingTable} (doc_id INTEGER PRIMARY KEY);",
        $"""
        CREATE VIRTUAL TABLE {FtsTable} USING fts5(
            {Rules.IndexedColumn},
            content = '', contentless_delete = 1,
            tokenize = 'trigram remove_diacritics 1');
        """,
    ];

    /// <summary>Removes the entry of <paramref name="id"/> (an SQL expression) from the index, if it is there.</summary>
    internal string RemoveSql(string id) =>
        $"DELETE FROM {FtsTable} WHERE rowid = (SELECT s_rowid FROM {EntryTable} WHERE doc_id = {id}); "
        + $"DELETE FROM {EntryTable} WHERE doc_id = {id};";

    /// <summary>
    /// Brings the entry of <paramref name="id"/> (an SQL expression) in step with its row: removes it, and indexes the
    /// row again if it is there and has a text.
    /// </summary>
    internal string RefreshSql(string id) => RemoveSql(id) + " " + Insert($"{_key} = {id}", $"s.doc_id = {id}");

    /// <summary>
    /// The three triggers that note the changed rows of the table in the outbox — for the application's own
    /// migration, or for <c>CreateTriggers()</c>. LUI creates them only when it is asked to (ADR-0015). They are plain
    /// SQL: any SQLite writes the table with them, and the index itself is written by <c>ApplyPending()</c> on LUI's
    /// checked connection.
    /// </summary>
    internal string TriggerScript() =>
        string.Join(Environment.NewLine, TriggerStatements().Select(trigger => trigger.Sql + ";"));

    /// <summary>The three triggers, each with its name and its <c>CREATE TRIGGER</c> statement as SQLite stores it.</summary>
    internal IReadOnlyList<(string Name, string Sql)> TriggerStatements()
    {
        var table = ContentTable;
        var key = Identifier.Quote(Link.Key);
        string[] watched = [Link.Key, Link.Text, .. new[] { Link.Weight, Link.Priority }.OfType<string>()];
        var columns = string.Join(", ", watched.Distinct(StringComparer.OrdinalIgnoreCase).Select(Identifier.Quote));
        var inserted = Trigger(IndexName, "ai");
        var updated = Trigger(IndexName, "au");
        var deleted = Trigger(IndexName, "ad");

        return
        [
            (inserted, $"CREATE TRIGGER {inserted} AFTER INSERT ON {table} BEGIN {Note($"new.{key}")} END"),
            (updated, $"CREATE TRIGGER {updated} AFTER UPDATE OF {columns} ON {table} BEGIN {Note($"old.{key}")} {Note($"new.{key}")} END"),
            (deleted, $"CREATE TRIGGER {deleted} AFTER DELETE ON {table} BEGIN {Note($"old.{key}")} END"),
        ];
    }

    /// <summary>
    /// The triggers of the index as SQLite stores them: <c>(name, sql)</c> — by their exact names, so that the triggers
    /// of an index <c>tags_all</c> are not taken for those of <c>tags</c>.
    /// </summary>
    internal string StoredTriggersSql =>
        TriggerCountSql(IndexName).Replace("SELECT count(*)", "SELECT name, sql", StringComparison.Ordinal);

    /// <summary>How many of the three triggers of an index are on its table.</summary>
    internal static string TriggerCountSql(string indexName) =>
        $"SELECT count(*) FROM sqlite_master WHERE type = 'trigger' AND name IN "
        + $"('{Trigger(indexName, "ai")}', '{Trigger(indexName, "au")}', '{Trigger(indexName, "ad")}');";

    /// <summary>Removes the triggers of <see cref="TriggerScript"/>, if they are there.</summary>
    internal static string DropTriggersSql(string indexName) =>
        $"DROP TRIGGER IF EXISTS {Trigger(indexName, "ai")}; DROP TRIGGER IF EXISTS {Trigger(indexName, "au")}; "
        + $"DROP TRIGGER IF EXISTS {Trigger(indexName, "ad")};";

    /// <summary>
    /// The statements that remove a <c>Linked</c> index: its tables, and the triggers, if the application had LUI create
    /// them. The application's table itself is not touched.
    /// </summary>
    internal static IReadOnlyList<string> Drop(string indexName) =>
    [
        DropTriggersSql(indexName),
        $"DROP TABLE IF EXISTS {Pending(indexName)};",
        $"DROP TABLE IF EXISTS {Fts(indexName)};",
        $"DROP TABLE IF EXISTS {Entries(indexName)};",
    ];

    private static string Entries(string indexName) => $"lui_{indexName}";

    private static string Pending(string indexName) => $"lui_{indexName}_pending";

    /// <summary>What a trigger does: it notes the key in the outbox — nothing else, no FTS, no normalisation.</summary>
    private string Note(string id) => $"INSERT OR IGNORE INTO {PendingTable}(doc_id) VALUES ({id});";

    private static string Fts(string indexName) => $"lui_{indexName}_fts";

    private static string Trigger(string indexName, string kind) => $"lui_{indexName}_{kind}";

    /// <summary>A row has a text the index can hold.</summary>
    private string HasText => $"{_text} IS NOT NULL AND {_text} <> ''";

    /// <summary>A key that can be a document id of the index: 0 … <see cref="MaxKey"/>. A row with another key is not indexed.</summary>
    private string KeyInRange => $"{_key} BETWEEN 0 AND {MaxKey.ToString(System.Globalization.CultureInfo.InvariantCulture)}";

    /// <summary>
    /// A row is indexed when it has a text and a key in range — a row that is neither is left out, never refused: a
    /// refusal in a trigger would roll back the application's own write, and in a sync the whole index (review of
    /// pull request 16, M2). <see cref="BatchRowsSql"/> counts what was left out.
    /// </summary>
    private string Indexable => $"{HasText} AND {KeyInRange}";

    /// <summary>
    /// The generation a single row is written with — the one the last sync took (<c>lui_meta.sync_generation</c>, ADR-0077). A refresh
    /// that runs beside a sync therefore writes the generation of that sync, and the removal at its end leaves the row.
    /// </summary>
    private string CurrentGeneration => $"(SELECT sync_generation FROM lui_meta WHERE index_name = '{IndexName}')";

    /// <summary>The packed rowid of a row (ADR-0006) — the same expression as the generated column of <c>Embedded</c>.</summary>
    private string RowIdValue => $"({_priority} << 61) | (({RowId.MaxWeight} - {_weight}) << 32) | {_key}";

    /// <summary>The two inserts that index the rows of a condition: the entry with its keys, then the search form.</summary>
    private string Insert(string rows, string entries)
    {
        var prefix = $"lower({Rules.PrefixValue(_norm)})";
        return
            $"INSERT INTO {EntryTable}(doc_id, s_rowid, s_p1, s_p2, gen) "
            + $"SELECT {_key}, {RowIdValue}, "
            + $"substr({prefix}, 1, 1), substr({prefix}, 1, 2), {CurrentGeneration} "
            + $"FROM {ContentTable} h WHERE {rows} AND {Indexable}; "
            + $"INSERT INTO {FtsTable}(rowid, {Rules.IndexedColumn}) "
            + $"SELECT s.s_rowid, {Rules.IndexedValue(_norm)} FROM {EntryTable} s JOIN {ContentTable} h ON {_key} = s.doc_id "
            + $"WHERE {entries} ORDER BY s.s_rowid;";
    }
}
