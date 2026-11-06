using Microsoft.Data.Sqlite;

namespace Nordmann.LUI.Querying;

/// <summary>The payload of a linked index without payload columns: there is none, and no column is selected.</summary>
internal sealed class NoPayload(string indexName) : PayloadReader
{
    internal override IReadOnlyList<string> Columns { get; } = [];

    internal override PayloadSource Source { get; } = PayloadSource.Built(indexName);

    internal override object? Read(SqliteDataReader row) => null;
}
