namespace Nordmann.LUI;

/// <summary>
/// An index exists with a definition other than the one that was asked for.
/// An index is never rebuilt silently (LUI-Spezifikation §7.1): the message
/// names both definitions.
/// </summary>
public sealed class LuiDefinitionMismatchException : InvalidOperationException
{
    /// <summary>Creates the exception.</summary>
    /// <param name="indexName">The name of the index.</param>
    /// <param name="stored">The definition in <c>lui_meta</c>.</param>
    /// <param name="requested">The definition the caller asked for.</param>
    public LuiDefinitionMismatchException(string indexName, string stored, string requested)
        : base(
            $"The index '{indexName}' exists with another definition.{Environment.NewLine}"
            + $"  stored:    {stored}{Environment.NewLine}"
            + $"  requested: {requested}{Environment.NewLine}"
            + "An index is never rebuilt silently. Ask for the stored definition, "
            + "or Drop() the index and ensure it again.")
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
