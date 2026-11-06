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
/// No type of the application is part of it (ADR-0035): the payload is bytes that the
/// serialiser of the catalog writes and reads, and its type is the application's to keep.
/// The kind of the key is — numbers or texts —, because it decides how a stored key is read.
/// Older definitions carry <c>payload</c> and <c>payloadType</c>; they are read and ignored.
/// </para>
/// </summary>
/// <param name="Kind">The kind of the index.</param>
/// <param name="Source">The content source: <c>Embedded</c> or <c>Linked</c>.</param>
/// <param name="KeyType">The kind of the key: <c>long</c> for numbers, <c>string</c> for texts.</param>
/// <param name="GermanTransliteration">Whether the text was stored with the German transliteration.</param>
/// <param name="FoldTable">The fold table the text was folded with.</param>
/// <param name="Link">The table of the application a linked index searches; <see langword="null"/> for an embedded one.</param>
internal sealed record IndexDefinition(
    LuiIndexKind Kind,
    string Source,
    string KeyType,
    bool GermanTransliteration,
    string FoldTable,
    LinkDefinition? Link = null)
{
    internal string ToJson() => JsonSerializer.Serialize(this, LuiJsonContext.Default.IndexDefinition);

    internal static IndexDefinition? FromJson(string json) =>
        JsonSerializer.Deserialize(json, LuiJsonContext.Default.IndexDefinition);
}
