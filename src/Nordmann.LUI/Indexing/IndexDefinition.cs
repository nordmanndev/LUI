using System.Text.Json;

namespace Nordmann.LUI.Indexing;

/// <summary>
/// What defines an index: everything that decides what its tables hold. It is
/// stored in <c>lui_meta</c> as JSON and compared on every <c>Ensure()</c>.
/// <para>
/// The similarity measure is not part of it: it is a default of the query and
/// changes no table.
/// </para>
/// </summary>
internal sealed record IndexDefinition(
    LuiIndexKind Kind,
    string Source,
    string KeyType,
    bool GermanTransliteration,
    bool Payload,
    string FoldTable)
{
    internal string ToJson() => JsonSerializer.Serialize(this, LuiJsonContext.Default.IndexDefinition);

    internal static IndexDefinition? FromJson(string json) =>
        JsonSerializer.Deserialize(json, LuiJsonContext.Default.IndexDefinition);
}
