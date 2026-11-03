using Nordmann.LUI.Indexing;

namespace Nordmann.LUI.Typos;

/// <summary>Where the typo dictionary of an <c>Owned</c> index reads its words from: the content table.</summary>
internal static class TypoSource
{
    /// <summary>The texts and weights of the index, one row at a time, on a connection of their own.</summary>
    internal static IEnumerable<(string Text, int Weight)> Read(LuiCatalog catalog, string indexName)
    {
        using var connection = catalog.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT text, weight FROM {OwnedSchema.Content(indexName)};";

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            yield return (reader.GetString(0), reader.GetInt32(1));
        }
    }
}
