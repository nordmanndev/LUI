namespace Nordmann.LUI;

/// <summary>
/// A key that is stored in an index does not convert to the key type of its definition (ADR-0073): a damaged file, not an
/// expected state. A key that LUI wrote always converts — an <c>int</c> index only takes <c>int</c> keys, a <c>guid</c> index
/// only GUIDs — so there is no <c>CheckKeys()</c>; the read that meets the damaged key fails, and the message names the index and
/// the stored key. The way out is the one of every damaged index: <see cref="LuiCatalog.DropIndex(string, bool)"/>, create it again and fill it.
/// </summary>
public sealed class LuiKeyException : LuiException
{
    internal LuiKeyException(string indexName, string storedKey, LuiKeyType keyType, string reason)
        : base(
            $"The stored key '{storedKey}' of the index '{indexName}' is not a valid key of type {keyType}: {reason} "
            + "The file is damaged — LUI only writes keys that convert. Drop the index (LuiCatalog.DropIndex) and fill it again.")
    {
        IndexName = indexName;
        StoredKey = storedKey;
        KeyType = keyType;
    }

    /// <summary>The index that holds the key.</summary>
    public string IndexName { get; }

    /// <summary>The key as the file stores it.</summary>
    public string StoredKey { get; }

    /// <summary>The key type the definition of the index names.</summary>
    public LuiKeyType KeyType { get; }
}
