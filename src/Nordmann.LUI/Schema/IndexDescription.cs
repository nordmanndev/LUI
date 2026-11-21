using Nordmann.LUI.Normalization;

namespace Nordmann.LUI.Schema;

/// <summary>
/// What a builder chain describes (ADR-0073): the kind, the options and — for a linked index — the link. The builders
/// hold nothing else; the catalog creates from it. <see cref="KeyKind"/> is set only by the old <c>WithKey</c>: a
/// definition takes its key type from its type argument. <see cref="PayloadMap"/> is the map of the old
/// <c>WithPayload</c> — code of the application, runtime only, never stored and never compared.
/// </summary>
/// <param name="Source">Embedded or linked.</param>
/// <param name="Kind">Single word or multi word.</param>
/// <param name="Options">The options, the similarity (runtime only) among them.</param>
/// <param name="Link">The link, with the payload columns; <see langword="null"/> for an embedded index.</param>
/// <param name="KeyKind">The kind of the key the old <c>WithKey</c> chose; <see langword="null"/> when nothing chose one.</param>
/// <param name="PayloadMap">The map of the old <c>WithPayload</c>; <see langword="null"/> for none.</param>
internal sealed record IndexDescription(
    LuiIndexSource Source,
    LuiIndexKind Kind,
    IndexOptions Options,
    LinkDefinition? Link,
    LuiKeyKind? KeyKind = null,
    Func<LuiPayloadRow, object?>? PayloadMap = null)
{
    /// <summary>The definition as <c>lui_meta</c> stores it — and as the declared one is compared with the stored one.</summary>
    /// <param name="keyType">The stored name of the key type.</param>
    /// <returns>The definition.</returns>
    internal IndexDefinition ToStored(string keyType) =>
        Source == LuiIndexSource.Embedded
            ? new IndexDefinition(
                Kind,
                EmbeddedSource.SourceName,
                keyType,
                Options.GermanTransliteration,
                TokenizerFold.Version,
                TypoDistance: Options.TypoDistance)
            : new IndexDefinition(
                Kind,
                LinkedSource.SourceName,
                keyType,
                Options.GermanTransliteration,
                TokenizerFold.Version,
                Link: Link,
                TypoDistance: Options.TypoDistance);
}
