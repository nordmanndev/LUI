using System.Text.Json.Serialization.Metadata;
using Nordmann.LUI.Indexes;
using Nordmann.LUI.Schema;
using Nordmann.LUI.Normalization;

namespace Nordmann.LUI;

/// <summary>
/// Describes an index whose content lives in a table of the library, <c>lui_&lt;name&gt;</c> — <c>Owned</c>, the
/// default (LUI-Spezifikation §5, §12.2): the application writes through the index. Every method is optional and
/// returns a builder; <see cref="Ensure"/> finishes it. An index over a table of the application starts here too,
/// with <c>LinkTo(…)</c>, as long as no key and no payload are chosen.
/// </summary>
/// <typeparam name="TKey">The key of the application: <see cref="long"/> (the default) or <see cref="string"/>.</typeparam>
/// <typeparam name="TPayload">The payload that comes back with a hit; <see cref="LuiNoPayload"/> until <c>WithPayload</c>.</typeparam>
public sealed class LuiIndexBuilder<TKey, TPayload>
{
    private readonly LuiCatalog _catalog;
    private readonly string _name;
    private readonly LuiIndexKind _kind;
    private readonly ILuiPayloadSerializer<TPayload>? _payload;
    private IndexSettings _settings;

    internal LuiIndexBuilder(
        LuiCatalog catalog,
        string name,
        LuiIndexKind kind,
        ILuiPayloadSerializer<TPayload>? payload = null,
        IndexSettings? settings = null)
    {
        _catalog = catalog;
        _name = name;
        _kind = kind;
        _payload = payload;
        _settings = settings ?? IndexSettings.Default;
    }

    /// <summary>The name of the index.</summary>
    internal string Name => _name;

    /// <summary>The kind of the index.</summary>
    internal LuiIndexKind Kind => _kind;

    /// <summary>The catalog of the index.</summary>
    internal LuiCatalog Catalog => _catalog;

    /// <summary>The settings made so far — what <c>WithKey</c>, <c>WithPayload</c> and <c>LinkTo</c> pass on.</summary>
    internal IndexSettings Settings => _settings;

    /// <summary>
    /// The key of the application: <see cref="long"/> by default, <see cref="string"/> for anything else that names an
    /// entry — a slug, a <see cref="Guid"/> as text, the text itself.
    /// </summary>
    /// <typeparam name="TNewKey"><see cref="long"/> or <see cref="string"/>.</typeparam>
    /// <returns>A builder of an index with that key, with the settings made so far.</returns>
    /// <exception cref="NotSupportedException"><typeparamref name="TNewKey"/> is neither <see cref="long"/> nor <see cref="string"/>.</exception>
    public LuiIndexBuilder<TNewKey, TPayload> WithKey<TNewKey>()
    {
        // the key type is checked here, where it is chosen, not when the index is ensured
        _ = KeyCodec<TNewKey>.KeyType;
        return new LuiIndexBuilder<TNewKey, TPayload>(_catalog, _name, _kind, _payload, _settings);
    }

    /// <summary>
    /// Stores a payload with every entry, serialised as JSON with a source-generated type description (AOT- and
    /// trim-safe). The payload is read for the hits only.
    /// </summary>
    /// <typeparam name="TNewPayload">The payload type.</typeparam>
    /// <param name="typeInfo">The generated type description of the payload.</param>
    /// <returns>A builder of an index with that payload, with the settings made so far.</returns>
    public LuiIndexBuilder<TKey, TNewPayload> WithPayload<TNewPayload>(JsonTypeInfo<TNewPayload> typeInfo) =>
        WithPayload(new LuiJsonPayloadSerializer<TNewPayload>(typeInfo));

    /// <summary>Stores a payload with every entry, serialised by <paramref name="serializer"/>.</summary>
    /// <typeparam name="TNewPayload">The payload type.</typeparam>
    /// <param name="serializer">The serialiser.</param>
    /// <returns>A builder of an index with that payload, with the settings made so far.</returns>
    public LuiIndexBuilder<TKey, TNewPayload> WithPayload<TNewPayload>(ILuiPayloadSerializer<TNewPayload> serializer)
    {
        ArgumentNullException.ThrowIfNull(serializer);
        return new LuiIndexBuilder<TKey, TNewPayload>(_catalog, _name, _kind, serializer, _settings);
    }

    /// <summary>
    /// The default similarity measure of the queries of this index. Default: Jaro-Winkler for a single-word
    /// index, N-gram Dice for a multi-word one. It is a property of <b>this instance</b>, not of the stored
    /// index: it changes no table, so it is not part of the definition, and two <c>Ensure()</c> calls with
    /// different measures give two instances that rank differently.
    /// </summary>
    /// <param name="similarity">The measure.</param>
    /// <returns>The builder.</returns>
    public LuiIndexBuilder<TKey, TPayload> Similarity(ILuiSimilarity similarity)
    {
        ArgumentNullException.ThrowIfNull(similarity);
        _settings = _settings with { Similarity = similarity };
        return this;
    }

    /// <summary>
    /// Turns the German transliteration (<c>ä → ae</c>, <c>ß → ss</c>) on or off. On by default; turn it off for
    /// corpora without German text (§6.1). Part of the definition: it decides the stored search form.
    /// </summary>
    /// <param name="enabled">Whether the transliteration is applied.</param>
    /// <returns>The builder.</returns>
    public LuiIndexBuilder<TKey, TPayload> GermanTransliteration(bool enabled)
    {
        _settings = _settings with { GermanTransliteration = enabled };
        return this;
    }

    /// <summary>
    /// Typo tolerance (§10): an input without a word start is corrected by one edit, with a dictionary of the
    /// word beginnings of the index. Off unless it is asked for — the dictionary costs memory (about 140 MB for
    /// 685,000 words) and is built when the first input needs it (or by <c>WarmUpTypoDictionary</c>). A property
    /// of <b>this instance</b>, not of the stored index; a query can override it. The dictionary itself belongs
    /// to the index of the catalog and is shared by all its instances.
    /// <para>
    /// The dictionary learns the words that are written through the library (<c>Upsert</c>, <c>UpsertMany</c>)
    /// when they are written. Writes past it — another process, direct SQL, a transaction that commits after the
    /// dictionary was built — reach it with <c>RebuildTypoDictionary()</c>; so does a transaction of the
    /// application that is <b>rolled back</b>: its words stay in the dictionary until the rebuild, and an input
    /// that is exactly the beginning of such a word is not corrected.
    /// </para>
    /// </summary>
    /// <param name="enabled">Whether an input without a word start is corrected.</param>
    /// <returns>The builder.</returns>
    public LuiIndexBuilder<TKey, TPayload> TypoTolerance(bool enabled = true)
    {
        _settings = _settings with { TypoTolerance = enabled };
        return this;
    }

    /// <summary>
    /// Creates the index, or opens it if it exists with the same definition.
    /// </summary>
    /// <returns>The index.</returns>
    /// <exception cref="LuiDefinitionMismatchException">The index exists with another definition.</exception>
    public ILuiOwnedIndex<TKey, TPayload> Ensure()
    {
        var definition = new IndexDefinition(
            _kind,
            OwnedSource.SourceName,
            KeyCodec<TKey>.KeyType,
            _settings.GermanTransliteration,
            _payload is not null,
            TokenizerFold.Version);

        var source = new OwnedSource(_name, definition);
        IndexStore.Ensure(_catalog.Database, source, _catalog.TimeProvider.GetUtcNow());

        return new OwnedIndex<TKey, TPayload>(
            _catalog.Database,
            _catalog.TypoDictionaries,
            source,
            _payload,
            _settings.Similarity ?? source.Rules.DefaultSimilarity,
            _settings.TypoTolerance);
    }
}
