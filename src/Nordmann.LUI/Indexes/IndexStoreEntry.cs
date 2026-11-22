using System.Globalization;
using Nordmann.LUI.Normalization;
using Nordmann.LUI.Schema;

namespace Nordmann.LUI.Indexes;

/// <summary>
/// What <c>lui_meta</c> holds about an index (ADR-0006, ADR-0033): its kind, its content source, its definition
/// and the LUI format version it was written with.
/// </summary>
/// <param name="Kind">The kind of the index.</param>
/// <param name="Source">The content source: <c>Embedded</c> or <c>Linked</c>.</param>
/// <param name="Definition">The definition of the index.</param>
/// <param name="SchemaVersion">The LUI format version the index was written with (<c>lui_meta.schema_version</c>, not SQLite's schema counter).</param>
internal sealed record IndexStoreEntry(
    LuiIndexKind Kind,
    string Source,
    IndexDefinition Definition,
    int SchemaVersion)
{
    /// <summary>
    /// The checks every stored index has to pass, whether it came from the file or from the definitions in memory: the LUI
    /// format version it was written with, a key type this version knows, and the folding it was stored with.
    /// </summary>
    /// <param name="name">The name of the index.</param>
    /// <exception cref="LuiDefinitionMismatchException">The LUI format, the key type or the folding is another one.</exception>
    internal void EnsureReadable(string name)
    {
        if (SchemaVersion != Meta.SchemaVersion)
        {
            throw Mismatch(
                name,
                $"the LUI format {SchemaVersion.ToString(CultureInfo.InvariantCulture)}",
                $"the LUI format {Meta.SchemaVersion.ToString(CultureInfo.InvariantCulture)}");
        }

        if (!KeyCodec.IsKnown(Definition.KeyType))
        {
            throw Mismatch(name, $"the key type {Definition.KeyType}", "the key type long, int, string or guid");
        }

        if (!string.Equals(Definition.FoldTable, TokenizerFold.Version, StringComparison.Ordinal))
        {
            throw Mismatch(name, $"the fold table {Definition.FoldTable}", $"the fold table {TokenizerFold.Version}");
        }
    }

    /// <summary>A mismatch of an index that is opened: it is never rebuilt silently, and the way out is another way to open it or a drop.</summary>
    /// <param name="name">The name of the index.</param>
    /// <param name="stored">What the file holds.</param>
    /// <param name="requested">What this version, or the caller, wants.</param>
    /// <returns>The exception.</returns>
    internal static LuiDefinitionMismatchException Mismatch(string name, string stored, string requested) =>
        new(
            name,
            stored,
            requested,
            "An index that is opened is never rebuilt silently: open it as what it is, or remove it with "
            + "LuiCatalog.DropIndex(name) and create it again.");
}
