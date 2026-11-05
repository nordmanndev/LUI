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
    /// <param name="stored">The definition in <c>lui_meta</c>, or what the check found.</param>
    /// <param name="requested">The definition the caller asked for, or what the check wants.</param>
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
                  + "with it — and create it again: then Sync() it, and create the triggers again if the application "
                  + "uses them."
                : "or remove the index with LuiCatalog.DropIndex(name) and create it again."))
    {
        IndexName = indexName;
        Stored = stored;
        Requested = requested;
    }

    /// <summary>
    /// Creates the exception for the read path (ADR-0033): the index exists, but it is not the index that was asked
    /// for — another schema version, another folding, another key type, another payload. An index that is opened is
    /// never rebuilt, so the way out is another way to open it.
    /// </summary>
    internal LuiDefinitionMismatchException(string indexName, string stored, string requested, string advice)
        : base(
            $"The index '{indexName}' exists with another definition.{Environment.NewLine}"
            + $"  stored:    {stored}{Environment.NewLine}"
            + $"  requested: {requested}{Environment.NewLine}"
            + advice)
    {
        IndexName = indexName;
        Stored = stored;
        Requested = requested;
    }

    /// <summary>The name of the index.</summary>
    public string IndexName { get; }

    /// <summary>
    /// The definition in <c>lui_meta</c>, as JSON — or what a check found, when the check is about the version of the
    /// schema, the folding, the key type or the payload (ADR-0033).
    /// </summary>
    public string Stored { get; }

    /// <summary>
    /// The definition the caller asked for, as JSON — or what a check wants, when the check is about the version of the
    /// schema, the folding, the key type or the payload (ADR-0033).
    /// </summary>
    public string Requested { get; }
}
