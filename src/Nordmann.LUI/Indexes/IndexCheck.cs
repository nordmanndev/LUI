using Microsoft.Data.Sqlite;
using Nordmann.LUI.Schema;

namespace Nordmann.LUI.Indexes;

/// <summary>
/// The parts of a check that every index has (LUI-Spezifikation §12.6, §14): the stored definition and FTS5's integrity
/// check. Each finding is a sentence that names what is wrong and what repairs it.
/// </summary>
internal static class IndexCheck
{
    /// <summary>The keys a finding names at most.</summary>
    internal const int Examples = 5;

    /// <summary><c>SQLITE_CORRUPT</c>: FTS5's integrity check reports a finding with it.</summary>
    private const int SqliteCorrupt = 11;

    /// <summary>
    /// Whether the index is stored with the definition of this instance. A finding when it is not stored at all — it was
    /// dropped — or stored with another definition.
    /// </summary>
    /// <returns><see langword="true"/> when the definition is stored and the same; the rest of a check needs that.</returns>
    internal static bool Definition(SqliteConnection connection, SqliteTransaction transaction, ContentSource source, List<string> problems)
    {
        var stored = IndexStore.ReadDefinition(connection, transaction, source.IndexName);
        if (stored is null)
        {
            problems.Add($"The index '{source.IndexName}' is not in lui_meta: it was dropped. Create it again and use the new instance.");
            return false;
        }

        if (stored != source.Definition)
        {
            problems.Add(
                $"The index '{source.IndexName}' is stored with another definition ({stored.ToJson()}) than this instance has "
                + $"({source.Definition.ToJson()}): it was dropped and created anew. Use the instance of the new Create().");
            return false;
        }

        return true;
    }

    /// <summary>FTS5's integrity check of the search index; it runs as an insert, so it needs the write lock.</summary>
    internal static void SearchIndex(SqliteConnection connection, SqliteTransaction transaction, ContentSource source, List<string> problems, string repair)
    {
        try
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = source.IntegrityCheckSql;
            command.ExecuteNonQuery();
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode == SqliteCorrupt)
        {
            problems.Add($"The search index of '{source.IndexName}' fails FTS5's integrity check ({exception.Message}): {repair}");
        }
    }

    /// <summary>A few keys of a finding, quoted: <c>'a', 'b', …</c>.</summary>
    internal static string Keys(IReadOnlyCollection<string> keys, long count) =>
        string.Join(", ", keys.Select(key => $"'{key}'")) + (count > keys.Count ? ", …" : string.Empty);
}
