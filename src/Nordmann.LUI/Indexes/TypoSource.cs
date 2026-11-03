namespace Nordmann.LUI.Indexes;

/// <summary>
/// Where the typo dictionary of an index reads its words from: the statement its content source gives
/// (<c>ContentSource.TextsSql</c>, <c>(text, weight)</c>).
/// </summary>
internal static class TypoSource
{
    /// <summary>The texts and weights of the index, one row at a time, on a connection of their own.</summary>
    internal static IEnumerable<(string Text, int Weight)> Read(LuiCatalog catalog, string sql)
    {
        using var connection = catalog.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = sql;

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            yield return (reader.GetString(0), reader.GetInt32(1));
        }
    }
}
