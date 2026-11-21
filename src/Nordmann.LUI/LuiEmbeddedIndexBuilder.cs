using Nordmann.LUI.Indexes;
using Nordmann.LUI.Normalization;
using Nordmann.LUI.Schema;

namespace Nordmann.LUI;

/// <summary>
/// Describes an index whose content lives in a table of the library, <c>lui_&lt;name&gt;</c> — <c>Embedded</c>, the
/// default (ADR-0014, ADR-0015, docs/api.md): the application writes through the index. Every method is optional and
/// returns a builder, and the builder only <b>describes</b> (ADR-0073): the catalog creates from the description. An index
/// over a table of the application starts here too, with <see cref="LinkTo"/>.
/// <para>
/// The key type of an index comes from its definition, <c>LuiIndexDefinition&lt;long&gt;</c> (ADR-0073); <see cref="WithKey"/>
/// is the old way and only the old calls of the catalog allow it.
/// </para>
/// <para>
/// The builder is the parameter of <see cref="LuiIndexDefinition{TKey}"/> (and of the old
/// <see cref="LuiCatalog.Create(string, Func{LuiIndexKindBuilder, LuiEmbeddedIndexBuilder})"/>): it is the description of an
/// index, not the way into it. It holds no catalog and no name.
/// </para>
/// </summary>
public sealed class LuiEmbeddedIndexBuilder
{
    private readonly LuiIndexKind _kind;
    private readonly bool _oldPath;
    private LuiKeyKind? _keyKind;
    private IndexOptions _options;

    internal LuiEmbeddedIndexBuilder(LuiIndexKind kind, bool oldPath)
    {
        _kind = kind;
        _oldPath = oldPath;
        _options = IndexOptions.Default;
    }

    /// <summary>The options made so far — what <c>LinkTo</c> passes on.</summary>
    internal IndexOptions Options => _options;

    /// <summary>
    /// What the keys are: numbers (<see cref="LuiKeyKind.Number"/>, the default) or texts (<see cref="LuiKeyKind.Text"/>)
    /// for anything else that names an entry — a slug, a ULID, a <see cref="Guid"/> as text. Part of the definition. It
    /// is the way of the old calls of the catalog: <b>inside an index definition it throws</b>
    /// <see cref="LuiConfigurationException"/> — the key type follows from the type argument, <c>LuiIndexDefinition&lt;TKey&gt;</c>.
    /// </summary>
    /// <param name="kind">The kind of the keys.</param>
    /// <returns>The builder.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="kind"/> is not a kind.</exception>
    /// <exception cref="LuiConfigurationException">The builder belongs to an index definition.</exception>
    public LuiEmbeddedIndexBuilder WithKey(LuiKeyKind kind)
    {
        if (!_oldPath)
        {
            throw new LuiConfigurationException(
                "WithKey is not part of an index definition: the key type is the type argument of the definition, "
                + "LuiIndexDefinition<TKey>.");
        }

        _keyKind = Setting.Defined(kind, nameof(kind));
        return this;
    }

    /// <summary>
    /// Links the index to a table of the application instead (<c>Linked</c>, ADR-0015): it searches the table's
    /// <paramref name="text"/>, its key is the table's <c>INTEGER PRIMARY KEY</c>. LUI does not change the table and
    /// keeps <b>no copy of the row</b>: its search index holds the normalised text, its own table the weight and the
    /// priority (they decide the order of the candidates); the text and the payload of a hit are read from the table when
    /// the hit is read. The application keeps the index current with <c>Sync</c> and <c>Refresh</c>, or with triggers it
    /// creates on purpose.
    /// </summary>
    /// <param name="table">The table of the application.</param>
    /// <param name="key">Its INTEGER PRIMARY KEY.</param>
    /// <param name="text">The column that is searched and shown.</param>
    /// <param name="weight">The column of the popularity (0 … 536,870,911); <see langword="null"/> for none.</param>
    /// <param name="priority">
    /// The column of the priority, with the values of <see cref="LuiPriority"/> — 0 is <see cref="LuiPriority.System"/>,
    /// 3 is <see cref="LuiPriority.Normal"/>, a smaller number ranks first; another value counts as normal.
    /// <see langword="null"/> for none: every entry is normal.
    /// </param>
    /// <returns>The builder of the linked index, with the settings made so far.</returns>
    /// <exception cref="ArgumentException">A name is not a name of the pattern <c>^[A-Za-z_][A-Za-z0-9_]{0,63}$</c>.</exception>
    /// <exception cref="InvalidOperationException">The keys were made texts: a linked index has the numbers of its table.</exception>
    public LuiLinkedIndexBuilder LinkTo(
        string table,
        string key,
        string text,
        string? weight = null,
        string? priority = null)
    {
        if (_keyKind == LuiKeyKind.Text)
        {
            throw new InvalidOperationException(
                "A linked index has the keys of its table — its INTEGER PRIMARY KEY: leave WithKey(LuiKeyKind.Text) out.");
        }

        var link = new LinkDefinition(
            Identifier.ValidateApplicationName(table, nameof(table)),
            Identifier.ValidateApplicationName(key, nameof(key)),
            Identifier.ValidateApplicationName(text, nameof(text)),
            weight is null ? null : Identifier.ValidateApplicationName(weight, nameof(weight)),
            priority is null ? null : Identifier.ValidateApplicationName(priority, nameof(priority)));

        return new LuiLinkedIndexBuilder(_kind, link, _options, _oldPath);
    }

    /// <summary>
    /// The default similarity measure of the queries of this index. Default: Jaro-Winkler for a single-word
    /// index, N-gram Dice for a multi-word one. It is a property of <b>this instance</b>, not of the stored
    /// index: it changes no table, so it is not part of the definition, and two <c>Create()</c> calls with
    /// different measures give two instances that rank differently.
    /// </summary>
    /// <param name="similarity">The measure.</param>
    /// <returns>The builder.</returns>
    public LuiEmbeddedIndexBuilder Similarity(ILuiSimilarity similarity)
    {
        ArgumentNullException.ThrowIfNull(similarity);
        _options = _options with { Similarity = similarity };
        return this;
    }

    /// <summary>
    /// Turns the German transliteration (<c>ä → ae</c>, <c>ß → ss</c>) on or off. On by default; turn it off for
    /// corpora without German text (ADR-0005). Part of the definition: it decides the stored search form.
    /// </summary>
    /// <param name="enabled">Whether the transliteration is applied.</param>
    /// <returns>The builder.</returns>
    public LuiEmbeddedIndexBuilder GermanTransliteration(bool enabled)
    {
        _options = _options with { GermanTransliteration = enabled };
        return this;
    }

    /// <summary>
    /// Typo tolerance (ADR-0012): an input without a word start is corrected, with a dictionary of the word
    /// beginnings of the index. The distance says <b>how many errors</b> are forgiven — an enumeration, not a
    /// number, because the dictionary grows with every additional error (194 MB for one error and 685,739 words,
    /// 388 MB for two; <c>docs/typo-dictionary.md</c>). Off unless it is asked for, and built when the
    /// first input needs it (or by <c>WarmUpTypoDictionary()</c>). The distance belongs to the <b>index</b> and
    /// is part of its stored definition; a query can only switch the correction on or off. The dictionary
    /// itself belongs to the index of the catalog and is shared by all its instances.
    /// <para>
    /// The dictionary learns the words that are written through the library (<c>Upsert</c>, <c>UpsertMany</c>)
    /// when they are written. Writes past it — another process, direct SQL, a transaction that commits after the
    /// dictionary was built — reach it with <c>RebuildTypoDictionary()</c>; so does a transaction of the
    /// application that is <b>rolled back</b>: its words stay in the dictionary until the rebuild, and an input
    /// that is exactly the beginning of such a word is not corrected.
    /// </para>
    /// </summary>
    /// <param name="distance">How many errors are forgiven; <see cref="LuiTypoDistance.None"/> corrects nothing.</param>
    /// <returns>The builder.</returns>
    public LuiEmbeddedIndexBuilder TypoTolerance(LuiTypoDistance distance = LuiTypoDistance.OneError)
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
    public ILuiEmbeddedIndex Create() =>
        throw new InvalidOperationException(
            "A builder describes an index and creates nothing: pass a definition to catalog.Create — "
            + "catalog.Create(new LuiIndexDefinition<long>(name, index => …)).");
#pragma warning restore CA1822

    /// <summary>What the chain described so far — what the catalog creates from.</summary>
    /// <returns>The description.</returns>
    internal IndexDescription Describe() =>
        new(LuiIndexSource.Embedded, _kind, _options, Link: null, _keyKind);
}
