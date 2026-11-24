using System.Globalization;

namespace Nordmann.LUI.Schema;

/// <summary>
/// The key between the application and the file (ADR-0035, ADR-0073): the file stores every key as text, and the definition
/// names the exact key type — <c>long</c>, <c>int</c>, <c>string</c> or <c>guid</c>. A <c>long</c> and an <c>int</c> are stored in
/// invariant digits, a <c>guid</c> in the canonical form <c>"D"</c>, lower case — so every stored key is canonical, there are no
/// duplicates of one GUID and no text that is not a GUID. A stored key that does not convert is a damaged file:
/// a <see cref="LuiKeyException"/>, never a quiet default.
/// </summary>
internal static class KeyCodec
{
    private const string LongName = "long";
    private const string IntName = "int";
    private const string StringName = "string";
    private const string GuidName = "guid";

    /// <summary>The name of a key type in the definition.</summary>
    internal static string Name(LuiKeyType keyType) =>
        keyType switch
        {
            LuiKeyType.Long => LongName,
            LuiKeyType.Int => IntName,
            LuiKeyType.String => StringName,
            LuiKeyType.Guid => GuidName,
            _ => throw new ArgumentOutOfRangeException(nameof(keyType), keyType, "Not a key type of LUI."),
        };

    /// <summary>Whether this version knows the name of a key type as the definition carries it.</summary>
    internal static bool IsKnown(string keyType) =>
        keyType is LongName or IntName or StringName or GuidName;

    /// <summary>
    /// The key type of a name in the definition. A name this version does not know is refused here, not read as numbers —
    /// the first hit would fail far from the cause (review of PR 60, M3).
    /// </summary>
    internal static LuiKeyType TypeOf(string keyType) =>
        keyType switch
        {
            LongName => LuiKeyType.Long,
            IntName => LuiKeyType.Int,
            StringName => LuiKeyType.String,
            GuidName => LuiKeyType.Guid,
            _ => throw new NotSupportedException(
                $"The key type '{keyType}' of a definition is none of 'long', 'int', 'string' and 'guid' (ADR-0073)."),
        };

    /// <summary>The stored key as text, checked against its type: a Guid in the canonical form; a text that is not a key of the type is a <see cref="LuiKeyException"/>.</summary>
    /// <param name="text">The stored key.</param>
    /// <param name="keyType">The key type of the index.</param>
    /// <param name="indexName">The index, for the message.</param>
    /// <returns>The stored text, checked: unchanged for a number or a string (<c>+05</c> stays <c>+05</c>), the canonical lower-case form for a GUID.</returns>
    internal static string FromText(string text, LuiKeyType keyType, string indexName)
    {
        switch (keyType)
        {
            case LuiKeyType.Long:
                return long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _)
                    ? text
                    : throw new LuiKeyException(indexName, text, keyType, "it is not a whole number in the range of a long.");
            case LuiKeyType.Int:
                return int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _)
                    ? text
                    : throw new LuiKeyException(indexName, text, keyType, "it is not a whole number in the range of an int.");
            case LuiKeyType.Guid:
                return Guid.TryParseExact(text, "D", out var guid)
                    ? guid.ToString("D", CultureInfo.InvariantCulture)
                    : throw new LuiKeyException(indexName, text, keyType, "it is not a GUID in the form 'D'.");
            default:
                return text;
        }
    }
}
