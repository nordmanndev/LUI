using Nordmann.LUI.Normalization;

namespace Nordmann.LUI.Schema;

/// <summary>
/// Where the content of an index lives (LUI-Spezifikation §5), and everything that depends on it: the
/// names of the tables, the statements that create and remove them, and the SQL that the query path and
/// the typo dictionary read with. The query path knows this class, not a table name — so a second source
/// (<c>Linked</c>, a table of the application) is a second subclass, not a second query path (ADR-0014).
/// <para>
/// Every statement keeps the columns the query path reads in the same places, whatever the source calls
/// them: the candidates <c>(rowid, text)</c>, the prefix path <c>(rowid, text)</c>, the hits
/// <c>(doc_id, key, payload …)</c>, the typo dictionary <c>(text, weight)</c>. Every name that reaches a
/// statement has been checked against <see cref="Identifier.Pattern"/>.
/// </para>
/// </summary>
internal abstract class ContentSource
{
    protected ContentSource(string indexName, IndexDefinition definition)
    {
        IndexName = indexName;
        Definition = definition;
        Rules = IndexKindRules.For(definition.Kind);
        Normalization = new NormalizationOptions { GermanTransliteration = definition.GermanTransliteration };
    }

    /// <summary>The name of the index.</summary>
    internal string IndexName { get; }

    /// <summary>The stored definition of the index.</summary>
    internal IndexDefinition Definition { get; }

    /// <summary>The rules of the kind of the index — everything that follows from the kind.</summary>
    internal IndexKindRules Rules { get; }

    /// <summary>The normalisation the content was stored with — the one every input is read with.</summary>
    internal NormalizationOptions Normalization { get; }

    /// <summary>
    /// Whether the text of an entry can change without the index (<c>Linked</c>: the application writes its table, and
    /// the index follows when it is told). Then every candidate is checked against its text as it is now.
    /// </summary>
    internal virtual bool TextCanChangeUnderTheIndex => false;

    /// <summary>The table that holds the texts and the generated search columns.</summary>
    internal abstract string ContentTable { get; }

    /// <summary>The FTS5 table over <see cref="IndexKindRules.IndexedColumn"/>.</summary>
    internal abstract string FtsTable { get; }

    /// <summary>The statements that create the index, in the order they have to run.</summary>
    internal abstract IReadOnlyList<string> CreateStatements();

    /// <summary>The prefix path (§8.1): <c>(rowid, text)</c> of the best entries whose text begins with one or two characters.</summary>
    internal abstract string PrefixSql(int length);

    /// <summary>The candidates of an input of three or more characters (§8.2): <c>(rowid, text)</c> in the order of the rowid.</summary>
    internal abstract string CandidateSql { get; }

    /// <summary>
    /// Key and payload of the hits (E7): <c>(doc_id, key, payload …)</c> for the doc_ids <paramref name="parameters"/>,
    /// the payload being <paramref name="payloadColumns"/> in their order.
    /// </summary>
    internal abstract string HitsSql(string parameters, IReadOnlyList<string> payloadColumns);

    /// <summary>
    /// The hits of the untyped facade (ADR-0033): the key as the text it is stored as, the payload as the raw JSON —
    /// the JSON object of the columns of a linked index. The default is the payload column of the content table.
    /// </summary>
    /// <param name="parameters">The placeholders of the document ids.</param>
    /// <returns>The statement that reads the key and the payload of the hits.</returns>
    internal virtual string DocumentHitsSql(string parameters) => HitsSql(parameters, ["payload"]);

    /// <summary>The number of entries.</summary>
    internal abstract string CountSql { get; }

    /// <summary>What the typo dictionary is built from: <c>(text, weight)</c> of every entry (§10.1).</summary>
    internal abstract string TextsSql { get; }

    /// <summary>Merges the segments of the search index into one (FTS5 <c>optimize</c>).</summary>
    internal string OptimizeSql => $"INSERT INTO {FtsTable}({FtsTable}) VALUES ('optimize');";

    /// <summary>
    /// Sets how eagerly FTS5 merges segments while it is written (<c>automerge</c>, 0 = not at all, 4 = SQLite's
    /// default). A sync turns it off while it loads and merges once at the end (ADR-0016).
    /// </summary>
    internal string AutomergeSql(int segments) =>
        $"INSERT INTO {FtsTable}({FtsTable}, rank) VALUES ('automerge', {segments.ToString(System.Globalization.CultureInfo.InvariantCulture)});";

    /// <summary>The integrity check of the search index (FTS5 <c>integrity-check</c>); an error is a finding.</summary>
    internal virtual string IntegrityCheckSql => $"INSERT INTO {FtsTable}({FtsTable}) VALUES ('integrity-check');";
}
