#pragma warning disable MA0048 // the generic form of a name that has a non-generic file: <Name>OfPayload.cs (ADR-0073)
using Nordmann.LUI.Schema;

namespace Nordmann.LUI;

/// <summary>
/// Describes an index over a table of the application <b>with a payload</b> (ADR-0073): what
/// <see cref="LuiLinkedIndexBuilder.WithPayload{TPayload}"/> returns. The map stays typed end to end — the cast per hit and the
/// "the map built an 'X'" error of the untyped path are gone — and the type decides what accepts the builder: a
/// <see cref="LuiLinkedIndexDefinition{TKey, TPayload}"/> only takes a builder that ends in the same
/// <typeparamref name="TPayload"/>, a <see cref="LuiLinkedIndexDefinition{TKey}"/> (no payload) takes none.
/// <para>
/// It does <b>not</b> derive from <see cref="LuiLinkedIndexBuilder"/> and has the same option methods, so the order of
/// <c>WithPayload</c> and the options is free. Like every builder it only describes: it holds no catalog and no name.
/// </para>
/// </summary>
/// <typeparam name="TPayload">What the map builds from a row of the table.</typeparam>
public sealed class LuiLinkedIndexBuilder<TPayload>
    where TPayload : notnull
{
    private readonly LuiIndexKind _kind;
    private readonly LinkDefinition _link;
    private readonly IReadOnlyList<string> _columns;
    private readonly Func<LuiPayloadRow, TPayload> _map;
    private IndexOptions _options;

    internal LuiLinkedIndexBuilder(
        LuiIndexKind kind,
        LinkDefinition link,
        IndexOptions options,
        IReadOnlyList<string> columns,
        Func<LuiPayloadRow, TPayload> map)
    {
        _kind = kind;
        _link = link;
        _options = options;
        _columns = columns;
        _map = map;
    }

    /// <summary>The options made so far — what <c>WithPayload</c> took over and the option methods changed.</summary>
    internal IndexOptions Options => _options;

    /// <summary>
    /// The default similarity measure of the queries of this index — a property of <b>this instance</b>, not stored and
    /// not compared, as for an embedded index.
    /// </summary>
    /// <param name="similarity">The measure.</param>
    /// <returns>The builder.</returns>
    public LuiLinkedIndexBuilder<TPayload> Similarity(ILuiSimilarity similarity)
    {
        ArgumentNullException.ThrowIfNull(similarity);
        _options = _options with { Similarity = similarity };
        return this;
    }

    /// <summary>Turns the German transliteration on or off (ADR-0005); part of the definition.</summary>
    /// <param name="enabled">Whether the transliteration is applied.</param>
    /// <returns>The builder.</returns>
    public LuiLinkedIndexBuilder<TPayload> GermanTransliteration(bool enabled)
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
    public LuiLinkedIndexBuilder<TPayload> TypoTolerance(LuiTypoDistance distance = LuiTypoDistance.OneError)
    {
        _options = _options with { TypoDistance = distance };
        return this;
    }

    /// <summary>What the chain described so far — what the catalog creates from. The map is code and is not stored.</summary>
    /// <returns>The description.</returns>
    internal IndexDescription Describe()
    {
        var map = _map;
        return new IndexDescription(
            LuiIndexSource.Linked,
            _kind,
            _options,
            _link with { PayloadColumns = _columns },
            KeyKind: null,
            row => map(row));
    }
}
#pragma warning restore MA0048
