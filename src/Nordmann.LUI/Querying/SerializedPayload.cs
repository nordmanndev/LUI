using Microsoft.Data.Sqlite;

namespace Nordmann.LUI.Querying;

/// <summary>The payload of an embedded index: the stored bytes, read by the hit with the serialiser of the catalog.</summary>
internal sealed class SerializedPayload(string indexName, ILuiPayloadSerializer? serializer) : PayloadReader
{
    internal override IReadOnlyList<string> Columns { get; } = ["payload"];

    internal override PayloadSource Source { get; } = PayloadSource.Serialized(indexName, serializer);

    internal override object? Read(SqliteDataReader row) =>
        row.IsDBNull(FirstColumn) ? null : row.GetFieldValue<byte[]>(FirstColumn);
}
