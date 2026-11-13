using Nordmann.LUI.Indexes;
using Nordmann.LUI.Normalization;
using Nordmann.LUI.Querying;
using Nordmann.LUI.Schema;

namespace Nordmann.LUI;

/// <summary>
/// Describes an index over a table of the application (<c>Linked</c>, ADR-0014, ADR-0015, ADR-0015, ADR-0015). It is
/// loosely coupled: <see cref="Create"/> creates LUI's own tables and only reads the application's table — no column,
/// no index, no trigger is added to it. Every method is optional and returns a builder.
/// <para>
/// What the index keeps (ADR-0035): <b>no copy of the row</b>. The search index holds the normalised text of the
/// table's text column; LUI's own table holds the weight and the priority, packed into the order of the candidates, and
/// the first letters. The text and the payload of a hit are read from the table when the hit is read: a change of a
/// payload column is visible at once, and so is a new text of a row that is still found. A new text becomes
/// <i>searchable</i>, and a new weight or priority takes its place in the order and in the hit, after <c>Refresh</c>,
/// <c>Sync</c> or the triggers.
/// </para>
/// <para>
/// The builder is reached through <c>LuiCatalog.CreateLinked(name, index =&gt; index.LinkTo(…))</c>: it is the description
/// of an index, not the way into it (ADR-0033).
/// </para>
/// </summary>
public sealed class LuiLinkedIndexBuilder
{
    private readonly LuiCatalog _catalog;
    private readonly string _name;
    private readonly LuiIndexKind _kind;
    private readonly LinkDefinition _link;
    private IReadOnlyList<string> _payloadColumns = [];
    private Func<LuiPayloadRow, object?>? _payload;
    private IndexOptions _options;

    internal LuiLinkedIndexBuilder(
        LuiCatalog catalog,
        string name,
        LuiIndexKind kind,
        LinkDefinition link,
        IndexOptions options)
    {
        _options = options;
        _catalog = catalog;
        _name = name;
        _kind = kind;
        _link = link;
    }

    /// <summary>The options made so far — what <c>LinkTo</c> passes on.</summary>
    internal IndexOptions Options => _options;

    /// <summary>
    /// Reads a payload from columns of the application's table, for the hits only: the columns are selected with them,
    /// and <paramref name="map"/> builds the payload from the row.
    /// </summary>
    /// <typeparam name="TPayload">What the map builds; the type is the application's to know when it reads the hit.</typeparam>
    /// <param name="columns">The columns the payload is built from.</param>
    /// <param name="map">Builds the payload from the row; it sees only <paramref name="columns"/>.</param>
    /// <returns>The builder.</returns>
    public LuiLinkedIndexBuilder WithPayload<TPayload>(IReadOnlyList<string> columns, Func<LuiPayloadRow, TPayload> map)
    {
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(map);
        ArgumentOutOfRangeException.ThrowIfZero(columns.Count, nameof(columns));
        if (columns.Distinct(StringComparer.OrdinalIgnoreCase).Count() != columns.Count)
        {
            // the row would see one of two equal names only (review of pull request 16, N7)
            throw new ArgumentException("A payload column is named twice.", nameof(columns));
        }

        _payloadColumns = [.. columns.Select(column => Identifier.ValidateApplicationName(column, nameof(columns)))];
        _payload = row => map(row);
        return this;
    }

    /// <summary>
    /// The default similarity measure of the queries of this index — a property of <b>this instance</b>, as for an
    /// embedded index.
    /// </summary>
    /// <param name="similarity">The measure.</param>
    /// <returns>The builder.</returns>
    public LuiLinkedIndexBuilder Similarity(ILuiSimilarity similarity)
    {
        ArgumentNullException.ThrowIfNull(similarity);
        _options = _options with { Similarity = similarity };
        return this;
    }

    /// <summary>Turns the German transliteration on or off (ADR-0005); part of the definition.</summary>
    /// <param name="enabled">Whether the transliteration is applied.</param>
    /// <returns>The builder.</returns>
    public LuiLinkedIndexBuilder GermanTransliteration(bool enabled)
    {
        _options = _options with { GermanTransliteration = enabled };
        return this;
    }

    /// <summary>
    /// Typo tolerance (ADR-0012), as for an embedded index. The dictionary learns the words of <c>Refresh</c> and <c>Sync</c>;
    /// a write to the table that LUI is not told of — without a refresh and without the triggers — reaches it with
    /// <c>RebuildTypoDictionary()</c>.
    /// </summary>
    /// <param name="distance">How many errors are forgiven; <see cref="LuiTypoDistance.None"/> corrects nothing.</param>
    /// <returns>The builder.</returns>
    public LuiLinkedIndexBuilder TypoTolerance(LuiTypoDistance distance = LuiTypoDistance.OneError)
    {
        _options = _options with { TypoDistance = distance };
        return this;
    }

    /// <summary>
    /// Creates LUI's tables of the index, or opens them if the index exists with the same definition — and checks the
    /// application's table, every time. The index starts empty: fill it with <c>Sync()</c>. That holds after a
    /// <c>DropIndex</c> too: a new linked index is empty and has no triggers until <c>Sync()</c> and, if wanted,
    /// <c>CreateTriggers()</c>.
    /// <para>
    /// The definition holds the columns of the payload (ADR-0033), not its type (ADR-0035): a linked index with a payload
    /// is opened with a map again, one without payload columns without one.
    /// </para>
    /// </summary>
    /// <returns>The index.</returns>
    /// <exception cref="LuiLinkedTableException">The table or a column is missing, or the key is not the table's INTEGER PRIMARY KEY.</exception>
    /// <exception cref="LuiDefinitionMismatchException">The index exists with another definition.</exception>
    /// <exception cref="LuiReadOnlyException">The file may not be written.</exception>
    public ILuiLinkedIndex Create()
    {
        // the columns of the payload are part of the definition (ADR-0033): a linked index with a payload can
        // only be opened when they are stored with it
        var definition = new IndexDefinition(
            _kind,
            LinkedSource.SourceName,
            KeyCodec.KeyType(LuiKeyKind.Number),
            _options.GermanTransliteration,
            TokenizerFold.Version,
            Link: _link with { PayloadColumns = _payloadColumns.Count == 0 ? null : _payloadColumns },
            TypoDistance: _options.TypoDistance);

        var source = new LinkedSource(_name, definition);
        IndexStore.Create(
            _catalog.Database,
            source,
            _catalog.TimeProvider.GetUtcNow(),
            (connection, transaction) => LinkedTable.Verify(connection, transaction, _link, _payloadColumns));

        PayloadReader payload = _payload is null
            ? new NoPayload(_name)
            : new ColumnPayload(_name, _payloadColumns, _payload);

        return new LinkedIndex(
            _catalog.Database,
            _catalog.TypoDictionaries,
            source,
            payload,
            _options.Similarity ?? source.Rules.DefaultSimilarity,
            _options.TypoDistance);
    }
}
