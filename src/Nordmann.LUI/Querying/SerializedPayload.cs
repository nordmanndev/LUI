using Microsoft.Data.Sqlite;

namespace Nordmann.LUI.Querying;

/// <summary>A payload stored as bytes by a serialiser (<c>Owned</c>); none without a serialiser.</summary>
/// <typeparam name="TPayload">The payload type.</typeparam>
internal sealed class SerializedPayload<TPayload>(ILuiPayloadSerializer<TPayload>? serializer) : PayloadReader<TPayload>
{
    internal override TPayload? Read(SqliteDataReader row) =>
        serializer is not null && !row.IsDBNull(FirstColumn)
            ? serializer.Deserialize(row.GetFieldValue<byte[]>(FirstColumn))
            : default;
}
