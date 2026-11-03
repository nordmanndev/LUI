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

    /// <summary>The number of entries.</summary>
    internal abstract string CountSql { get; }

    /// <summary>What the typo dictionary is built from: <c>(text, weight)</c> of every entry (§10.1).</summary>
    internal abstract string TextsSql { get; }
}
