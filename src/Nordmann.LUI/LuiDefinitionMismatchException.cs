namespace Nordmann.LUI;

/// <summary>
/// An index exists with a definition other than the one that was asked for.
/// An index is never rebuilt silently (LUI-Spezifikation §7.1): the message
/// names both definitions.
/// </summary>
public sealed class LuiDefinitionMismatchException : LuiException
{
    /// <summary>Creates the exception.</summary>
    /// <param name="indexName">The name of the index.</param>
    /// <param name="stored">The definition in <c>lui_meta</c>.</param>
    /// <param name="requested">The definition the caller asked for.</param>
    public LuiDefinitionMismatchException(string indexName, string stored, string requested)
        : this(indexName, stored, requested, linked: false)
    {
    }

    /// <summary>Creates the exception, with the way out of a linked index (review of pull request 16, F2).</summary>
    internal LuiDefinitionMismatchException(string indexName, string stored, string requested, bool linked)
        : base(
            $"The index '{indexName}' exists with another definition.{Environment.NewLine}"
            + $"  stored:    {stored}{Environment.NewLine}"
            + $"  requested: {requested}{Environment.NewLine}"
            + "An index is never rebuilt silently. Ask for the stored definition, "
            + (linked
                ? "or remove the index with LuiCatalog.DropIndex(name, dropTriggers: true) — its triggers on the table go "
                  + "with it — and ensure it again: then Sync() it, and create the triggers again if the application "
                  + "uses them."
                : "or remove the index with LuiCatalog.DropIndex(name) and ensure it again."))
    {
        IndexName = indexName;
        Stored = stored;
        Requested = requested;
    }

    /// <summary>The name of the index.</summary>
    public string IndexName { get; }

    /// <summary>The definition in <c>lui_meta</c>, as JSON.</summary>
    public string Stored { get; }

    /// <summary>The definition the caller asked for, as JSON.</summary>
    public string Requested { get; }
}
