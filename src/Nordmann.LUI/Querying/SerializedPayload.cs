using Microsoft.Data.Sqlite;

namespace Nordmann.LUI.Querying;

/// <summary>A payload stored as bytes by a serialiser (<c>Owned</c>); none without a serialiser.</summary>
/// <typeparam name="TPayload">The payload type.</typeparam>
internal sealed class SerializedPayload<TPayload>(ILuiPayloadSerializer<TPayload>? serializer) : PayloadReader<TPayload>
{
    // without a serialiser there is nothing to read, and the column is not even selected
    internal override IReadOnlyList<string> Columns { get; } = serializer is null ? [] : ["payload"];

    internal override TPayload? Read(SqliteDataReader row) =>
        serializer is not null && !row.IsDBNull(FirstColumn)
            ? serializer.Deserialize(row.GetFieldValue<byte[]>(FirstColumn))
            : default;
}
