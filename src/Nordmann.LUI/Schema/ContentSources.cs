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
            + "remove what it does not know."),
    };
}
