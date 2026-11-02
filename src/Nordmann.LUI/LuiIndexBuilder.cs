using System.Text.Json.Serialization.Metadata;
using Microsoft.Data.Sqlite;
using Nordmann.LUI.Indexing;
using Nordmann.LUI.Normalization;
using Nordmann.LUI.Payloads;
using Nordmann.LUI.Similarity;
using Measures = Nordmann.LUI.Similarity.Similarity;

namespace Nordmann.LUI;

/// <summary>
/// Describes an index (LUI-Spezifikation §12.2). Every method returns the
/// builder; <see cref="Ensure"/> finishes it.
/// </summary>
/// <typeparam name="TKey">The key of the application: <see cref="long"/> or <see cref="string"/>.</typeparam>
/// <typeparam name="TPayload">The payload that comes back with a hit.</typeparam>
public sealed class LuiIndexBuilder<TKey, TPayload>
{
    private readonly LuiCatalog _catalog;
    private readonly string _name;
    private IndexKind? _kind;
    private bool _owned;
    private IPayloadSerializer<TPayload>? _payload;
    private ITextSimilarity? _similarity;
    private bool _germanTransliteration = true;

    internal LuiIndexBuilder(LuiCatalog catalog, string name)
    {
        _catalog = catalog;
        _name = name;
    }

    /// <summary>
    /// A single-word index: tags, words, compounds, hashtags. The input is
    /// searched as a whole.
    /// </summary>
    /// <returns>The builder.</returns>
    public LuiIndexBuilder<TKey, TPayload> SingleWord()
    {
        _kind = IndexKind.SingleWord;
        return this;
    }

    /// <summary>
    /// A multi-word index: names and titles. The input is split into words that are
    /// ANDed, and it is also searched compactly, so the order of the words and the
    /// spaces do not matter (<c>kuchen apfel</c> and <c>apfelkuch</c> both find
    /// „Apfel Kuchen"). The default similarity is N-gram Dice.
    /// </summary>
    /// <returns>The builder.</returns>
    public LuiIndexBuilder<TKey, TPayload> MultiWord()
    {
        _kind = IndexKind.MultiWord;
        return this;
    }

    /// <summary>
    /// The content lives in a table of the library, <c>lui_&lt;name&gt;</c>;
    /// the application writes through the index (§5).
    /// </summary>
    /// <returns>The builder.</returns>
    public LuiIndexBuilder<TKey, TPayload> Owned()
    {
        _owned = true;
        return this;
    }

    /// <summary>
    /// Stores a payload with every entry, serialised as JSON with a
    /// source-generated type description (AOT- and trim-safe).
    /// </summary>
    /// <param name="typeInfo">The generated type description of the payload.</param>
    /// <returns>The builder.</returns>
    public LuiIndexBuilder<TKey, TPayload> Payload(JsonTypeInfo<TPayload> typeInfo) =>
        Payload(new JsonPayloadSerializer<TPayload>(typeInfo));

    /// <summary>Stores a payload with every entry, serialised by <paramref name="serializer"/>.</summary>
    /// <param name="serializer">The serialiser.</param>
    /// <returns>The builder.</returns>
    public LuiIndexBuilder<TKey, TPayload> Payload(IPayloadSerializer<TPayload> serializer)
    {
        ArgumentNullException.ThrowIfNull(serializer);
        _payload = serializer;
        return this;
    }

    /// <summary>
    /// The default similarity measure of the queries of this index. Default:
    /// Jaro-Winkler for a single-word index. It is a property of <b>this
    /// instance</b>, not of the stored index: it changes no table, so it is not
    /// part of the definition, and two <c>Ensure()</c> calls with different
    /// measures give two instances that rank differently.
    /// </summary>
    /// <param name="similarity">The measure.</param>
    /// <returns>The builder.</returns>
    public LuiIndexBuilder<TKey, TPayload> Similarity(ITextSimilarity similarity)
    {
        ArgumentNullException.ThrowIfNull(similarity);
        _similarity = similarity;
        return this;
    }

    /// <summary>
    /// Turns the German transliteration (<c>ä → ae</c>, <c>ß → ss</c>) on or
    /// off. On by default; turn it off for corpora without German text (§6.1).
    /// </summary>
    /// <param name="enabled">Whether the transliteration is applied.</param>
    /// <returns>The builder.</returns>
    public LuiIndexBuilder<TKey, TPayload> GermanTransliteration(bool enabled)
    {
        _germanTransliteration = enabled;
        return this;
    }

    /// <summary>
    /// Creates the index, or opens it if it exists with the same definition.
    /// </summary>
    /// <returns>The index.</returns>
    /// <exception cref="LuiDefinitionMismatchException">The index exists with another definition.</exception>
    public ILuiIndex<TKey, TPayload> Ensure()
    {
        var definition = Describe();

        using var scope = _catalog.Begin();
        using var connection = _catalog.OpenConnection();
        using var transaction = connection.BeginTransaction(deferred: false);

        var stored = LuiCatalog.ReadDefinition(connection, transaction, _name);
        if (stored is null)
        {
            foreach (var statement in OwnedSchema.Create(_name, definition))
            {
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = statement;
                command.ExecuteNonQuery();
            }

            _catalog.WriteDefinition(connection, transaction, _name, definition);
        }
        else if (stored != definition)
        {
            throw new LuiDefinitionMismatchException(_name, stored.ToJson(), definition.ToJson());
        }

        transaction.Commit();

        return new OwnedIndex<TKey, TPayload>(
            _catalog,
            _name,
            definition,
            _payload,
            _similarity ?? Measures.DefaultFor(definition.Kind));
    }

    /// <summary>
    /// Removes the index with its tables, indexes and triggers and its row in
    /// <c>lui_meta</c>. It works whatever the stored definition is — it is the
    /// way out of a <see cref="LuiDefinitionMismatchException"/>. Index objects that
    /// were created before do not survive a <c>Drop()</c>: ensure the index again
    /// and use the new object.
    /// </summary>
    /// <returns><see langword="true"/> when the index existed.</returns>
    public bool Drop()
    {
        using var scope = _catalog.Begin();
        using var connection = _catalog.OpenConnection();
        using var transaction = connection.BeginTransaction(deferred: false);

        var existed = LuiCatalog.ReadDefinition(connection, transaction, _name) is not null;

        foreach (var statement in OwnedSchema.Drop(_name))
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = statement;
            command.ExecuteNonQuery();
        }

        using (var meta = connection.CreateCommand())
        {
            meta.Transaction = transaction;
            meta.CommandText = "DELETE FROM lui_meta WHERE index_name = $name;";
            meta.Parameters.AddWithValue("$name", _name);
            meta.ExecuteNonQuery();
        }

        transaction.Commit();
        return existed;
    }

    private IndexDefinition Describe()
    {
        if (_kind is null)
        {
            throw new InvalidOperationException(
                $"The index '{_name}' needs a kind: call SingleWord() or MultiWord() before Ensure().");
        }

        if (!_owned)
        {
            throw new InvalidOperationException(
                $"The index '{_name}' needs a content source: call Owned() before Ensure().");
        }

        return new IndexDefinition(
            _kind.Value,
            OwnedSchema.Source,
            KeyCodec<TKey>.KeyType,
            _germanTransliteration,
            _payload is not null,
            TokenizerFold.Version);
    }
}
