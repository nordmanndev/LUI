namespace Nordmann.LUI.Indexes;

/// <summary>
/// The runtime of one index in a catalog (ADR-0075): what is built once per index and closed definition type and kept — the
/// index with its source, its rules, its query runner (candidate reader, typo correction, hit reader with its prepared SQL),
/// its sync and its check, the typo dictionary of the catalog by its name — and the guard that tells whether the definition
/// it was built from is still the one in the file. A handle is only the thin object on top: the catalog hands out the same one
/// for the same definition.
/// </summary>
/// <param name="Index">The index: <see cref="EmbeddedIndex"/> or <see cref="LinkedIndex"/>; it is thread-safe.</param>
/// <param name="Guard">The guard of its operations.</param>
internal sealed record IndexRuntime(IMaintainedIndex Index, IndexGuard Guard);
