namespace Nordmann.LUI;

/// <summary>
/// An index as it is read, without knowing its types (ADR-0033): the key comes as the text it is stored as, the
/// payload as the raw JSON. It is what a caller gets that does not know — or does not care — whether the key of the
/// index is a number or a text, and whether the index has a payload at all.
/// <para>
/// The index behind it is <b>opened</b>, not created: nothing is written, no table is created, and the file is never
/// locked for writing. The similarity is the default of the kind of the index, and the typo tolerance is off — a
/// query that wants a correction asks for it (<see cref="LuiQueryOptions.TypoTolerance"/>).
/// </para>
/// </summary>
public interface ILuiDocumentIndex
{
    /// <summary>The name of the index.</summary>
    string Name { get; }

    /// <summary>How the index is searched.</summary>
    LuiIndexKind Kind { get; }

    /// <summary>
    /// Whether a query of this index corrects a typo unless it says otherwise. Off for an index that was opened: a
    /// query that wants a correction asks for it (<see cref="LuiQueryOptions.TypoTolerance"/>).
    /// </summary>
    bool TypoTolerance { get; }

    /// <summary>The number of entries.</summary>
    /// <returns>The count.</returns>
    long Count();

    /// <summary>Runs a query — with the default options, or with options that come from a file or a form.</summary>
    /// <param name="text">What the user typed.</param>
    /// <param name="options">The options; <see langword="null"/> takes the defaults.</param>
    /// <param name="cancellationToken">Checked while the candidates are read.</param>
    /// <returns>The hits, with the key as text and the payload as the raw JSON.</returns>
    IReadOnlyList<LuiDocumentHit> Search(
        string text,
        LuiQueryOptions? options = null,
        CancellationToken cancellationToken = default);
}
