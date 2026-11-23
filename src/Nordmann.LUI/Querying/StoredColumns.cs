using Microsoft.Data.Sqlite;

namespace Nordmann.LUI.Querying;

/// <summary>
/// The payload columns of a linked index as the stored definition names them, and nothing else (ADR-0076): the runtime by name needs the
/// columns so that the check of the structure can see that the table still has them, but it has no map and reads no payload — a hit is never
/// read through it, so <see cref="Read"/> has nothing to build.
/// </summary>
/// <param name="indexName">The index.</param>
/// <param name="columns">The payload columns of the stored definition.</param>
internal sealed class StoredColumns(string indexName, IReadOnlyList<string> columns) : PayloadReader
{
    internal override IReadOnlyList<string> Columns { get; } = columns;

    internal override PayloadSource Source { get; } = PayloadSource.Built(indexName);

    internal override object? Read(SqliteDataReader row) => null;
}
