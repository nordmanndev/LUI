using Nordmann.LUI.Querying;
using Nordmann.LUI.Schema;
using Nordmann.LUI.Storage;
using Nordmann.LUI.Typos;

namespace Nordmann.LUI.Indexes;

/// <summary>
/// An index as it is read, without knowing its types (ADR-0033): the key comes as the text it is stored as, the
/// payload as the raw JSON. It writes nothing: it opens the index of the definition and hands out hits.
/// </summary>
internal sealed class DocumentIndex : ILuiDocumentIndex
{
    private static readonly LuiQueryOptions DefaultOptions = new();

    private readonly Database _database;
    private readonly ContentSource _source;
    private readonly QueryRunner<string, string?> _runner;

    internal DocumentIndex(
        Database database,
        TypoDictionaries typoDictionaries,
        ContentSource source,
        ILuiSimilarity similarity)
    {
        _database = database;
        _source = source;
        Name = source.IndexName;
        Kind = source.Rules.Kind;

        // the typo dictionary is not built here: it belongs to the index of the catalog and is built when a query
        // needs a correction (ADR-0033)
        var typos = typoDictionaries.For(Name, source.Normalization, () => TypoSource.Read(database, source.TextsSql));
        _runner = new QueryRunner<string, string?>(
            database,
            source,
            new RawJsonPayload(),
            similarity,
            typos,
            typoDefault: false);
    }

    public string Name { get; }

    public LuiIndexKind Kind { get; }

    public bool TypoTolerance => _runner.TypoTolerance;

    public long Count()
    {
        using var scope = _database.Begin();
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = _source.CountSql;
        return (long)command.ExecuteScalar()!;
    }

    public IReadOnlyList<LuiDocumentHit> Search(
        string text,
        LuiQueryOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var hits = _runner.Execute(text, options ?? DefaultOptions, cancellationToken).Hits;
        var documents = new LuiDocumentHit[hits.Count];
        for (var i = 0; i < hits.Count; i++)
        {
            documents[i] = Document(hits[i]);
        }

        return documents;
    }

    /// <summary>A hit of the query as a document: the key as the text it is stored as, the payload as the raw JSON.</summary>
    private static LuiDocumentHit Document(LuiHit<string, string?> hit) =>
        new()
        {
            Key = hit.Key,
            Text = hit.Text,
            Payload = hit.Payload,
            Weight = hit.Weight,
            Priority = hit.Priority,
            Similarity = hit.Similarity,
            AtWordStart = hit.AtWordStart,
        };
}
