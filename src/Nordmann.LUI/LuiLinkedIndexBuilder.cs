using Nordmann.LUI.Indexes;
using Nordmann.LUI.Normalization;
using Nordmann.LUI.Querying;
using Nordmann.LUI.Schema;

namespace Nordmann.LUI;

/// <summary>
/// Describes an index over a table of the application (<c>Linked</c>, ADR-0014, ADR-0015, ADR-0015, ADR-0015). It is
/// loosely coupled: the catalog creates LUI's own tables and only reads the application's table — no column, no index,
/// no trigger is added to it. Every method is optional and returns a builder, and the builder only <b>describes</b>
/// (ADR-0073): it holds no catalog and no name.
/// <para>
/// What the index keeps (ADR-0035): <b>no copy of the row</b>. The search index holds the normalised text of the
/// table's text column; LUI's own table holds the weight and the priority, packed into the order of the candidates, and
/// the first letters. The text and the payload of a hit are read from the table when the hit is read: a change of a
/// payload column is visible at once, and so is a new text of a row that is still found. A new text becomes
/// <i>searchable</i>, and a new weight or priority takes its place in the order and in the hit, after <c>Refresh</c>,
/// <c>Sync</c> or the triggers.
/// </para>
/// <para>
/// The builder is the parameter of <see cref="LuiLinkedIndexDefinition{TKey}"/> (and of the old
/// <c>LuiCatalog.CreateLinked(name, index =&gt; index.LinkTo(…))</c>): it is the description of an index, not the way into it.
/// </para>
/// </summary>
public sealed class LuiLinkedIndexBuilder
{
    private readonly LuiIndexKind _kind;
    private readonly LinkDefinition _link;
    private readonly bool _oldPath;
    private IReadOnlyList<string> _payloadColumns = [];
    private Func<LuiPayloadRow, object?>? _payload;
    private IndexOptions _options;

    internal LuiLinkedIndexBuilder(LuiIndexKind kind, LinkDefinition link, IndexOptions options, bool oldPath)
    {
        _options = options;
        _kind = kind;
        _link = link;
        _oldPath = oldPath;
    }

    /// <summary>The options made so far — what <c>LinkTo</c> passes on.</summary>
    internal IndexOptions Options => _options;

    /// <summary>
    /// Reads a payload from columns of the application's table, for the hits only: the columns are selected with them,
    /// and <paramref name="map"/> builds the payload from the row. It is the way of the old calls of the catalog: <b>inside an
    /// index definition it throws</b> <see cref="LuiConfigurationException"/> — the payload variants of the definitions come with
    /// issue #269.
    /// </summary>
    /// <typeparam name="TPayload">What the map builds; the type is the application's to know when it reads the hit.</typeparam>
    /// <param name="columns">The columns the payload is built from.</param>
    /// <param name="map">Builds the payload from the row; it sees only <paramref name="columns"/>.</param>
    /// <returns>The builder.</returns>
    /// <exception cref="LuiConfigurationException">The builder belongs to an index definition.</exception>
    public LuiLinkedIndexBuilder WithPayload<TPayload>(IReadOnlyList<string> columns, Func<LuiPayloadRow, TPayload> map)
    {
        if (!_oldPath)
        {
            throw new LuiConfigurationException(
                "WithPayload is not part of an index definition of this version: the payload variants of the definitions "
                + "(LuiLinkedIndexDefinition<TKey, TPayload>) come with issue #269.");
        }

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
    /// Not a way to create an index (ADR-0073): the builder describes, the catalog creates. Kept until the old calls are
    /// removed (issue #268); it throws.
    /// </summary>
    /// <returns>Never: it throws.</returns>
    /// <exception cref="InvalidOperationException">Always — pass a definition to <c>catalog.Create</c>.</exception>
#pragma warning disable CA1822 // an instance member on purpose: `builder.Create()` stays callable until the old calls go (issue #268)
    public ILuiLinkedIndex Create() =>
        throw new InvalidOperationException(
            "A builder describes an index and creates nothing: pass a definition to catalog.Create — "
            + "catalog.Create(new LuiLinkedIndexDefinition<long>(name, index => …)).");
#pragma warning restore CA1822

    /// <summary>What the chain described so far — what the catalog creates from.</summary>
    /// <returns>The description.</returns>
    internal IndexDescription Describe() =>
        new(
            LuiIndexSource.Linked,
            _kind,
            _options,
            _link with { PayloadColumns = _payloadColumns.Count == 0 ? null : _payloadColumns },
            KeyKind: null,
            _payload);
}
