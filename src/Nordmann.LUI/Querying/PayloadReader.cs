using Microsoft.Data.Sqlite;

namespace Nordmann.LUI.Querying;

/// <summary>
/// Reads the payload of a hit out of its row (ADR-0035): the stored bytes of an embedded index, or what the map of a
/// linked index builds out of its columns. Nothing is deserialised here — the hit does that when it is asked.
/// </summary>
internal abstract class PayloadReader
{
    /// <summary>The first column of the payload in the row of a hit: after <c>doc_id</c> and <c>key</c>.</summary>
    internal const int FirstColumn = 2;

    /// <summary>The columns the payload is read from.</summary>
    internal abstract IReadOnlyList<string> Columns { get; }

    /// <summary>Where the payloads come from, for the hits.</summary>
    internal abstract PayloadSource Source { get; }

    /// <summary>The payload of the row; <see langword="null"/> when it has none.</summary>
    internal abstract object? Read(SqliteDataReader row);
}
