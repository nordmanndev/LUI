using System.Globalization;

namespace Nordmann.LUI.Schema;

/// <summary>
/// The key between the application and the file (ADR-0035): the file stores every key as text, the definition says
/// whether the texts are numbers (<c>"long"</c>) or texts (<c>"string"</c>) — the names stay those the files already
/// carry.
/// </summary>
internal static class KeyCodec
{
    /// <summary>The name of a kind in the definition.</summary>
    internal static string KeyType(LuiKeyKind kind) => kind == LuiKeyKind.Text ? "string" : "long";

    /// <summary>The names of the kinds this version knows, as the definition carries them.</summary>
    internal static bool IsKnown(string keyType) =>
        string.Equals(keyType, "long", StringComparison.Ordinal) || string.Equals(keyType, "string", StringComparison.Ordinal);

    /// <summary>
    /// The kind of a name in the definition. A name this version does not know is refused here, not read as numbers —
    /// the first hit would fail far from the cause (review of PR 60, M3).
    /// </summary>
    internal static LuiKeyKind KindOf(string keyType) =>
        keyType switch
        {
            "long" => LuiKeyKind.Number,
            "string" => LuiKeyKind.Text,
            _ => throw new NotSupportedException(
                $"The key type '{keyType}' of a definition is neither 'long' nor 'string' (LUI-Spezifikation §12.3)."),
        };

    /// <summary>The stored text of a key; a key of the other kind is refused.</summary>
    internal static string ToText(LuiKey key, LuiKeyKind kind, string indexName)
    {
        if (key.Kind != kind)
        {
            throw new ArgumentException(
                $"The index '{indexName}' has keys that are {Describe(kind)}; the key '{key}' is {Describe(key.Kind)}.",
                nameof(key));
        }

        return key.ToString();
    }

    /// <summary>The key of a stored text.</summary>
    internal static LuiKey FromText(string text, LuiKeyKind kind) =>
        kind == LuiKeyKind.Number ? long.Parse(text, CultureInfo.InvariantCulture) : text;

    private static string Describe(LuiKeyKind kind) => kind == LuiKeyKind.Text ? "texts" : "numbers";
}
