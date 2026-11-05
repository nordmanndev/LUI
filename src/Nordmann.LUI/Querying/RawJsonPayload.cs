using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;

namespace Nordmann.LUI.Querying;

/// <summary>
/// The payload of the untyped facade (ADR-0033): the raw JSON, exactly as it was stored — the bytes of an embedded
/// index read as text, the JSON object of the columns of a linked one. <see langword="null"/> when the index stores
/// no payload.
/// </summary>
internal sealed class RawJsonPayload : PayloadReader<string?>
{
    /// <summary>The column of the hits statement the payload is read from.</summary>
    internal override IReadOnlyList<string> Columns { get; } = ["payload"];

    /// <summary>The payload of the current row as text; <see langword="null"/> when the row has none.</summary>
    internal override string? Read(SqliteDataReader row) =>
        row.IsDBNull(FirstColumn) ? null : AsText(row.GetValue(FirstColumn));

    /// <summary>The stored bytes as text, and the text of a JSON object as it is.</summary>
    private static string AsText(object value) => value switch
    {
        byte[] bytes => Encoding.UTF8.GetString(bytes),
        string text => text,
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
    };
}
