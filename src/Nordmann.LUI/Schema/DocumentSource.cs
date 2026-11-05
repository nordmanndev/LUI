namespace Nordmann.LUI.Schema;

/// <summary>
/// The source of the untyped facade (ADR-0033): the source of the index, with the hits of the facade — the key as
/// the text it is stored as, the payload as the raw JSON. Everything else is the source it wraps: the candidates, the
/// prefix path, the count and the texts of the typo dictionary.
/// </summary>
internal sealed class DocumentSource : ContentSource
{
    private readonly ContentSource _inner;

    internal DocumentSource(ContentSource inner)
        : base(inner.IndexName, inner.Definition)
    {
        _inner = inner;
    }

    /// <inheritdoc />
    internal override string ContentTable => _inner.ContentTable;

    /// <inheritdoc />
    internal override string FtsTable => _inner.FtsTable;

    /// <inheritdoc />
    internal override string CandidateSql => _inner.CandidateSql;

    /// <inheritdoc />
    internal override string CountSql => _inner.CountSql;

    /// <inheritdoc />
    internal override string TextsSql => _inner.TextsSql;

    /// <inheritdoc />
    internal override string PrefixSql(int length) => _inner.PrefixSql(length);

    /// <inheritdoc />
    internal override IReadOnlyList<string> CreateStatements() => _inner.CreateStatements();

    /// <summary>The hits of the facade: the key as the text it is stored as, the payload as the raw JSON.</summary>
    internal override string HitsSql(string parameters, IReadOnlyList<string> payloadColumns) =>
        _inner.DocumentHitsSql(parameters);
}
