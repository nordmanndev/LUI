using Microsoft.Data.Sqlite;

namespace Nordmann.LUI.Querying;

/// <summary>The payload of an embedded index: the stored bytes, read by the hit with the serialisation of the index.</summary>
internal sealed class SerializedPayload(string indexName, PayloadSerialization serialization) : PayloadReader
{
    internal override IReadOnlyList<string> Columns { get; } = ["payload"];

    internal override PayloadSource Source { get; } = PayloadSource.Serialized(indexName, serialization);

    internal override object? Read(SqliteDataReader row) =>
        row.IsDBNull(FirstColumn) ? null : row.GetFieldValue<byte[]>(FirstColumn);
}
