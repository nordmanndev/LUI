using System.Text.Json;

namespace Nordmann.LUI.Schema;

/// <summary>
/// What defines an index: everything that decides what its tables hold. It is
/// stored in <c>lui_meta</c> as JSON and compared on every <c>Create()</c>.
/// <para>
/// The similarity measure is not part of it: it is a default of the query and
/// changes no table.
/// </para>
/// <para>
/// The type of the payload is part of it (ADR-0033): the index stores the type
/// of the application it was created with, and an index that is opened with
/// another type is a mismatch that names both. The serialiser itself is not
/// stored — it is an object of the application, not of the file.
/// </para>
/// </summary>
/// <param name="Kind">The kind of the index.</param>
/// <param name="Source">The content source: <c>Embedded</c> or <c>Linked</c>.</param>
/// <param name="KeyType">The key type of the application: <c>long</c> or <c>string</c>.</param>
/// <param name="GermanTransliteration">Whether the text was stored with the German transliteration.</param>
/// <param name="Payload">Whether the index stores a payload.</param>
/// <param name="FoldTable">The fold table the text was folded with.</param>
/// <param name="PayloadType">The full name of the type of the payload; <see langword="null"/> when the index has none.</param>
/// <param name="Link">The table of the application a linked index searches; <see langword="null"/> for an embedded one.</param>
internal sealed record IndexDefinition(
    LuiIndexKind Kind,
    string Source,
    string KeyType,
    bool GermanTransliteration,
    bool Payload,
    string FoldTable,
    string? PayloadType = null,
    LinkDefinition? Link = null)
{
    internal string ToJson() => JsonSerializer.Serialize(this, LuiJsonContext.Default.IndexDefinition);

    internal static IndexDefinition? FromJson(string json) =>
        JsonSerializer.Deserialize(json, LuiJsonContext.Default.IndexDefinition);
}
