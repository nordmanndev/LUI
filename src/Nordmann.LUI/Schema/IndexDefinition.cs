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
/// No type of the application is part of it — with one exception (ADR-0072, which supersedes ADR-0035 for these two fields): an
/// <b>embedded</b> index with a declared payload type stores the <b>name of the payload type</b> and the <b>format</b> of the
/// serializer that wrote its payloads, and both are compared, so neither another type nor the same type in another format is read
/// silently. A linked index stores neither: its payload is built from the columns of its table by code of the application. The
/// settings of the serializer are not stored, only its format name. The kind of the key is part of it, because it decides how a
/// stored key is read. Older definitions carry <c>payload</c> and <c>payloadType</c>; they are read and ignored — the name of the payload
/// type has a field of its own, <c>payloadTypeName</c>, that no older definition has.
/// </para>
/// </summary>
/// <param name="Kind">The kind of the index.</param>
/// <param name="Source">The content source: <c>Embedded</c> or <c>Linked</c>.</param>
/// <param name="KeyType">The kind of the key: <c>long</c> for numbers, <c>string</c> for texts.</param>
/// <param name="GermanTransliteration">Whether the text was stored with the German transliteration.</param>
/// <param name="FoldTable">The fold table the text was folded with.</param>
/// <param name="Link">The table of the application a linked index searches; <see langword="null"/> for an embedded one.</param>
/// <param name="TypoDistance">
/// How many errors a query of this index forgives (ADR-0012). It is part of the definition, because the
/// dictionary of the word beginnings is built with it and a reopened index has to know how.
/// A definition written before this field carries <see cref="LuiTypoDistance.None"/> — an index that
/// corrected nothing.
/// </param>
/// <param name="PayloadTypeName">
/// The stable name of the payload type of an embedded index (<see cref="LuiPayloadNameAttribute"/> or the computed name);
/// <see langword="null"/> for an index without a declared payload type, and for every linked index.
/// </param>
/// <param name="PayloadFormat">
/// The format of the serializer that writes the payloads of an embedded index (<c>ILuiPayloadSerializer&lt;T&gt;.Format</c>);
/// <see langword="null"/> exactly when <paramref name="PayloadTypeName"/> is.
/// </param>
internal sealed record IndexDefinition(
    LuiIndexKind Kind,
    string Source,
    string KeyType,
    bool GermanTransliteration,
    string FoldTable,
    LinkDefinition? Link = null,
    LuiTypoDistance TypoDistance = LuiTypoDistance.None,
    string? PayloadTypeName = null,
    string? PayloadFormat = null)
{
    internal string ToJson() => JsonSerializer.Serialize(this, LuiJsonContext.Default.IndexDefinition);

    internal static IndexDefinition? FromJson(string json) =>
        JsonSerializer.Deserialize(json, LuiJsonContext.Default.IndexDefinition);
}
