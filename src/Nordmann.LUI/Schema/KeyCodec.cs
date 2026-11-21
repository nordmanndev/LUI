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

    /// <summary>Whether the keys of a type are numbers or texts in the file: <c>long</c> and <c>int</c> are numbers.</summary>
    internal static LuiKeyKind KindOf(LuiKeyType keyType) =>
        keyType is LuiKeyType.Long or LuiKeyType.Int ? LuiKeyKind.Number : LuiKeyKind.Text;

    /// <summary>The kind of the keys of a name in the definition; a name this version does not know is refused.</summary>
    internal static LuiKeyKind KindOf(string keyType) => KindOf(TypeOf(keyType));

    /// <summary>
    /// The stored text of a key; a key of the other kind, an <c>int</c> key outside the <c>int</c> range and a text that is no
    /// GUID in the form <c>"D"</c> are refused. A GUID is stored in lower case.
    /// </summary>
    internal static string ToText(LuiKey key, LuiKeyType keyType, string indexName)
    {
        var kind = KindOf(keyType);
        if (key.Kind != kind)
        {
            throw new ArgumentException(
                $"The index '{indexName}' has keys that are {Describe(kind)}; the key '{key}' is {Describe(key.Kind)}.",
                nameof(key));
        }

        switch (keyType)
        {
            case LuiKeyType.Int when key.ToInt64() is < int.MinValue or > int.MaxValue:
                throw new ArgumentException(
                    $"The index '{indexName}' has keys of type int; the key '{key}' is outside {int.MinValue} … {int.MaxValue}.",
                    nameof(key));
            case LuiKeyType.Guid:
                return Guid.TryParseExact(key.ToText(), "D", out var guid)
                    ? guid.ToString("D", CultureInfo.InvariantCulture)
                    : throw new ArgumentException(
                        $"The index '{indexName}' has keys that are GUIDs; the key '{key}' is not one in the form 'D' (00000000-0000-0000-0000-000000000000).",
                        nameof(key));
            default:
                return key.ToString();
        }
    }

    /// <summary>The key of a stored text; a text that does not convert is a damaged file.</summary>
    /// <exception cref="LuiKeyException">The stored text is no key of the type.</exception>
    internal static LuiKey FromText(string text, LuiKeyType keyType, string indexName)
    {
        switch (keyType)
        {
            case LuiKeyType.Long:
                return long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var number)
                    ? number
                    : throw new LuiKeyException(indexName, text, keyType, "it is not a whole number in the range of a long.");
            case LuiKeyType.Int:
                return int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var small)
                    ? small
                    : throw new LuiKeyException(indexName, text, keyType, "it is not a whole number in the range of an int.");
            case LuiKeyType.Guid:
                return Guid.TryParseExact(text, "D", out var guid)
                    ? guid.ToString("D", CultureInfo.InvariantCulture)
                    : throw new LuiKeyException(indexName, text, keyType, "it is not a GUID in the form 'D'.");
            default:
                return text;
        }
    }

    private static string Describe(LuiKeyKind kind) => kind == LuiKeyKind.Text ? "texts" : "numbers";
}
