using Nordmann.LUI.Normalization;

namespace Nordmann.LUI.Schema;

/// <summary>
/// The content source <c>Linked</c> (LUI-Spezifikation §5, §7.3, ADR-0015): the content lives in a table of the
/// application, and LUI never changes that table on its own — no column, no index, no trigger unless the
/// application asks for one. LUI keeps two tables of its own: <c>lui_&lt;name&gt;</c> without text (the document
/// id, the packed rowid and the two prefix keys) and a <b>contentless</b> FTS5 table over the search form. The text
/// and the payload are read from the application's table by its primary key, so the text is not stored twice.
/// <para>
/// The forms are computed by the same SQL expressions that generate the columns of <c>Owned</c>, inside the
/// statements that write the index: <see cref="RefreshSql"/> for one row, <see cref="SyncChunkSql"/> for a range of
/// rows — and, if the application wants them, the same statements in three triggers (<see cref="TriggerScript"/>).
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

    internal LinkedSource(string indexName, IndexDefinition definition)
        : base(indexName, definition)
    {
        Link = definition.Link
            ?? throw new ArgumentException("A linked index needs the table it is linked to.", nameof(definition));

        ContentTable = Identifier.Quote(Link.Table);
        FtsTable = Fts(indexName);
        EntryTable = Entries(indexName);
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

    /// <summary>The table of the application and its columns.</summary>
    internal LinkDefinition Link { get; }

    /// <summary>The quoted table of the application.</summary>
    internal override string ContentTable { get; }

    internal override string FtsTable { get; }

    /// <summary>The table of LUI: <c>(doc_id, s_rowid, s_p1, s_p2)</c>, no text.</summary>
    internal string EntryTable { get; }

    internal override string CandidateSql { get; }

    internal override string CountSql { get; }

    /// <summary>The texts of the entries that are in the index — not of every row of the table.</summary>
    internal override string TextsSql { get; }

    /// <summary>The text and the weight of one row, if it would be indexed: <c>(text, weight)</c> for <c>$id</c>.</summary>
    internal string ReadRowSql => $"SELECT {_text}, {_weight} FROM {ContentTable} h WHERE {_key} = $id AND {Indexable};";

    /// <summary>Clears the index before a <see cref="SyncChunkSql"/> fills it anew.</summary>
    internal string ClearSql => $"DELETE FROM {EntryTable}; INSERT INTO {FtsTable}({FtsTable}) VALUES ('delete-all');";

    /// <summary>The key that ends the next chunk of a sync: the <c>$size</c>-th key after <c>$from</c>, or none.</summary>
    internal string ChunkEndSql =>
        $"SELECT {_key} FROM {ContentTable} h WHERE {_key} > $from ORDER BY {_key} LIMIT 1 OFFSET $size - 1;";

    /// <summary>Indexes the rows with a key in <c>($from, $to]</c> — the search index in the order of the rowid.</summary>
    internal string SyncChunkSql => Insert($"{_key} > $from AND {_key} <= $to", "s.doc_id > $from AND s.doc_id <= $to");

    internal string OptimizeSql => $"INSERT INTO {FtsTable}({FtsTable}) VALUES ('optimize');";

    internal override string PrefixSql(int length) =>
        $"SELECT s.s_rowid, {_text} FROM {EntryTable} s JOIN {ContentTable} h ON {_key} = s.doc_id "
        + $"WHERE s.s_p{length} = $q ORDER BY s.s_rowid LIMIT $n;";

    internal override string HitsSql(string parameters, IReadOnlyList<string> payloadColumns) =>
        $"SELECT {_key}, {_key}{string.Concat(payloadColumns.Select(column => $", h.{Identifier.Quote(column)}"))} "
        + $"FROM {ContentTable} h WHERE {_key} IN ({parameters});";

    internal override IReadOnlyList<string> CreateStatements() =>
    [
        $"""
        CREATE TABLE {EntryTable} (
            doc_id  INTEGER PRIMARY KEY CHECK (doc_id BETWEEN 0 AND {RowId.MaxDocId}),
            s_rowid INTEGER NOT NULL UNIQUE,
            s_p1    TEXT    NOT NULL,
            s_p2    TEXT    NOT NULL
        );
        """,
        $"CREATE INDEX {EntryTable}_p1 ON {EntryTable}(s_p1, s_rowid);",
        $"CREATE INDEX {EntryTable}_p2 ON {EntryTable}(s_p2, s_rowid);",
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
    /// The three triggers that keep the index in step with the table — for the application's own migration, or
    /// for <c>CreateTriggers()</c>. LUI creates them only when it is asked to (ADR-0015).
    /// </summary>
    internal string TriggerScript()
    {
        var table = ContentTable;
        var key = Identifier.Quote(Link.Key);
        string[] watched = [Link.Key, Link.Text, .. new[] { Link.Weight, Link.Priority }.OfType<string>()];
        var columns = string.Join(", ", watched.Distinct(StringComparer.OrdinalIgnoreCase).Select(Identifier.Quote));

        return $"""
            CREATE TRIGGER {Trigger(IndexName, "ai")} AFTER INSERT ON {table} BEGIN
                {RefreshSql($"new.{key}")}
            END;
            CREATE TRIGGER {Trigger(IndexName, "au")} AFTER UPDATE OF {columns} ON {table} BEGIN
                {RemoveSql($"old.{key}")}
                {RefreshSql($"new.{key}")}
            END;
            CREATE TRIGGER {Trigger(IndexName, "ad")} AFTER DELETE ON {table} BEGIN
                {RemoveSql($"old.{key}")}
            END;
            """;
    }

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
        $"DROP TABLE IF EXISTS {Fts(indexName)};",
        $"DROP TABLE IF EXISTS {Entries(indexName)};",
    ];

    private static string Entries(string indexName) => $"lui_{indexName}";

    private static string Fts(string indexName) => $"lui_{indexName}_fts";

    private static string Trigger(string indexName, string kind) => $"lui_{indexName}_{kind}";

    /// <summary>A row is indexed when it has a text.</summary>
    private string Indexable => $"{_text} IS NOT NULL AND {_text} <> ''";

    /// <summary>The two inserts that index the rows of a condition: the entry with its keys, then the search form.</summary>
    private string Insert(string rows, string entries)
    {
        var prefix = $"lower({Rules.PrefixValue(_norm)})";
        return
            $"INSERT INTO {EntryTable}(doc_id, s_rowid, s_p1, s_p2) "
            + $"SELECT {_key}, ({_priority} << 61) | (({RowId.MaxWeight} - {_weight}) << 32) | {_key}, "
            + $"substr({prefix}, 1, 1), substr({prefix}, 1, 2) "
            + $"FROM {ContentTable} h WHERE {rows} AND {Indexable}; "
            + $"INSERT INTO {FtsTable}(rowid, {Rules.IndexedColumn}) "
            + $"SELECT s.s_rowid, {Rules.IndexedValue(_norm)} FROM {EntryTable} s JOIN {ContentTable} h ON {_key} = s.doc_id "
            + $"WHERE {entries} ORDER BY s.s_rowid;";
    }
}
