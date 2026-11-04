using Nordmann.LUI.Indexes;
using Nordmann.LUI.Normalization;
using Nordmann.LUI.Querying;
using Nordmann.LUI.Schema;

namespace Nordmann.LUI;

/// <summary>
/// Describes an index over a table of the application (<c>Linked</c>, LUI-Spezifikation §5, §7.3, ADR-0015). It is
/// loosely coupled: <see cref="Ensure"/> creates LUI's own tables and only reads the application's table — no column,
/// no index, no trigger is added to it. Every method is optional and returns a builder.
/// </summary>
/// <typeparam name="TPayload">The payload that comes back with a hit; <see cref="LuiNoPayload"/> until <c>WithPayload</c>.</typeparam>
public sealed class LuiLinkedIndexBuilder<TPayload>
{
    private readonly LuiCatalog _catalog;
    private readonly string _name;
    private readonly LuiIndexKind _kind;
    private readonly LinkDefinition _link;
    private readonly IReadOnlyList<string> _payloadColumns;
    private readonly Func<LuiPayloadRow, TPayload>? _payload;
    private IndexOptions _options;

    internal LuiLinkedIndexBuilder(
        LuiCatalog catalog,
        string name,
        LuiIndexKind kind,
        LinkDefinition link,
        IReadOnlyList<string> payloadColumns,
        Func<LuiPayloadRow, TPayload>? payload,
        IndexOptions options)
    {
        _options = options;
        _catalog = catalog;
        _name = name;
        _kind = kind;
        _link = link;
        _payloadColumns = payloadColumns;
        _payload = payload;
    }

    /// <summary>
    /// Reads a payload from columns of the application's table, for the hits only: the columns are selected with them,
    /// and <paramref name="map"/> builds the payload from the row.
    /// </summary>
    /// <typeparam name="TNewPayload">The payload type.</typeparam>
    /// <param name="columns">The columns the payload is built from.</param>
    /// <param name="map">Builds the payload from the row; it sees only <paramref name="columns"/>.</param>
    /// <returns>A builder of an index with that payload, with the settings made so far.</returns>
    public LuiLinkedIndexBuilder<TNewPayload> WithPayload<TNewPayload>(IReadOnlyList<string> columns, Func<LuiPayloadRow, TNewPayload> map)
    {
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(map);
        ArgumentOutOfRangeException.ThrowIfZero(columns.Count, nameof(columns));
        if (columns.Distinct(StringComparer.OrdinalIgnoreCase).Count() != columns.Count)
        {
            // the row would see one of two equal names only (review of pull request 16, N7)
            throw new ArgumentException("A payload column is named twice.", nameof(columns));
        }

        return new LuiLinkedIndexBuilder<TNewPayload>(
            _catalog,
            _name,
            _kind,
            _link,
            [.. columns.Select(column => Identifier.ValidateApplicationName(column, nameof(columns)))],
            map,
            _options);
    }

    /// <summary>
    /// The default similarity measure of the queries of this index — a property of <b>this instance</b>, as for an
    /// embedded index.
    /// </summary>
    /// <param name="similarity">The measure.</param>
    /// <returns>The builder.</returns>
    public LuiLinkedIndexBuilder<TPayload> Similarity(ILuiSimilarity similarity)
    {
        ArgumentNullException.ThrowIfNull(similarity);
        _options = _options with { Similarity = similarity };
        return this;
    }

    /// <summary>Turns the German transliteration on or off (§6.1); part of the definition.</summary>
    /// <param name="enabled">Whether the transliteration is applied.</param>
    /// <returns>The builder.</returns>
    public LuiLinkedIndexBuilder<TPayload> GermanTransliteration(bool enabled)
    {
        _options = _options with { GermanTransliteration = enabled };
        return this;
    }

    /// <summary>
    /// Typo tolerance (§10), as for an embedded index. The dictionary learns the words of <c>Refresh</c> and <c>Sync</c>;
    /// a write to the table that LUI is not told of — without a refresh and without the triggers — reaches it with
    /// <c>RebuildTypoDictionary()</c>.
    /// </summary>
    /// <param name="enabled">Whether an input without a word start is corrected.</param>
    /// <returns>The builder.</returns>
    public LuiLinkedIndexBuilder<TPayload> TypoTolerance(bool enabled = true)
    {
        _options = _options with { TypoTolerance = enabled };
        return this;
    }

    /// <summary>The options made so far — what <c>WithPayload</c> passes on.</summary>
    internal IndexOptions Options => _options;

    /// <summary>
    /// Creates LUI's tables of the index, or opens them if the index exists with the same definition — and checks the
    /// application's table, every time. The index starts empty: fill it with <c>Sync()</c>. That holds after a
    /// <c>DropIndex</c> too: a new linked index is empty and has no triggers until <c>Sync()</c> and, if wanted,
    /// <c>CreateTriggers()</c>.
    /// </summary>
    /// <returns>The index.</returns>
    /// <exception cref="LuiLinkedTableException">The table or a column is missing, or the key is not the table's INTEGER PRIMARY KEY.</exception>
    /// <exception cref="LuiDefinitionMismatchException">The index exists with another definition.</exception>
    public ILuiLinkedIndex<TPayload> Ensure()
    {
        var definition = new IndexDefinition(
            _kind,
            LinkedSource.SourceName,
            KeyCodec<long>.KeyType,
            _options.GermanTransliteration,
            Payload: false,
            TokenizerFold.Version,
            _link);

        var source = new LinkedSource(_name, definition);
        IndexStore.Ensure(
            _catalog.Database,
            source,
            _catalog.TimeProvider.GetUtcNow(),
            (connection, transaction) => LinkedTable.Verify(connection, transaction, _link, _payloadColumns));

        PayloadReader<TPayload> payload = _payload is null
            ? new SerializedPayload<TPayload>(null)
            : new ColumnPayload<TPayload>(_payloadColumns, _payload);

        return new LinkedIndex<TPayload>(
            _catalog.Database,
            _catalog.TypoDictionaries,
            source,
            payload,
            _options.Similarity ?? source.Rules.DefaultSimilarity,
            _options.TypoTolerance);
    }
}
