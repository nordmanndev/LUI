using Nordmann.LUI.Indexing;

namespace Nordmann.LUI;

/// <summary>
/// The second step of describing an index (LUI-Spezifikation §5, §12.2): where its
/// content lives.
/// </summary>
public sealed class LuiIndexSourceBuilder
{
    private readonly LuiCatalog _catalog;
    private readonly string _name;
    private readonly LuiIndexKind _kind;

    internal LuiIndexSourceBuilder(LuiCatalog catalog, string name, LuiIndexKind kind)
    {
        _catalog = catalog;
        _name = name;
        _kind = kind;
    }

    /// <summary>
    /// The content lives in a table of the library, <c>lui_&lt;name&gt;</c>; the application writes through
    /// the index (<see cref="ILuiOwnedIndex{TKey, TPayload}"/>). The index starts without a payload;
    /// <c>WithPayload(…)</c> gives it one.
    /// </summary>
    /// <typeparam name="TKey">The key of the application: <see cref="long"/> or <see cref="string"/>.</typeparam>
    /// <returns>The builder of the index.</returns>
    /// <exception cref="NotSupportedException"><typeparamref name="TKey"/> is neither <see cref="long"/> nor <see cref="string"/>.</exception>
    public LuiOwnedIndexBuilder<TKey, LuiNoPayload> Owned<TKey>()
    {
        // the key type is checked here, where it is chosen, not when the index is ensured
        _ = KeyCodec<TKey>.KeyType;
        return new LuiOwnedIndexBuilder<TKey, LuiNoPayload>(_catalog, _name, _kind);
    }
}
