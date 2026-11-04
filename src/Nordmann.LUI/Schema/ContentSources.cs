namespace Nordmann.LUI.Schema;

/// <summary>What is decided by the name of a content source, before there is a source object.</summary>
internal static class ContentSources
{
    /// <summary>
    /// The statements that remove an index — by the source of its stored definition, and as <c>Embedded</c> when there is
    /// none: the way out of a <see cref="LuiDefinitionMismatchException"/> works whatever the definition is. A source
    /// this version does not know is refused rather than guessed (review of pull request 14, N2).
    /// </summary>
    internal static IReadOnlyList<string> DropStatements(string indexName, IndexDefinition? stored) => stored?.Source switch
    {
        null or EmbeddedSource.SourceName => EmbeddedSource.Drop(indexName),
        LinkedSource.SourceName => LinkedSource.Drop(indexName),
        var other => throw new InvalidOperationException(
            $"The index '{indexName}' has the content source '{other}', which this version of LUI does not know; it does not "
            + "remove what it does not know. " + WayOut),
    };

    /// <summary>Whether this version knows the content source of a stored definition (<see langword="null"/>: none was stored).</summary>
    internal static bool IsKnown(string? source) => source is null or EmbeddedSource.SourceName or LinkedSource.SourceName;

    /// <summary>
    /// What to do with a database whose index has a content source this version does not know — another build of LUI
    /// created it, a pre-release one or a newer version (review of pull request 46, ADR-0023). Said where the refusal
    /// is, because the refusal is all that the person who meets it sees.
    /// </summary>
    internal const string WayOut =
        "The database was created by another build of LUI — a pre-release one, or a newer version: open it with that build, "
        + "or create the database anew.";
}
