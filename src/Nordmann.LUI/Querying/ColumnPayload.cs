using Microsoft.Data.Sqlite;

namespace Nordmann.LUI.Querying;

/// <summary>
/// A payload built from columns of the application's table (<c>Linked</c>, ADR-0035): the columns
/// are selected with the hits, in this order, and the mapping turns them into the payload.
/// </summary>
internal sealed class ColumnPayload : PayloadReader
{
    private readonly Func<LuiPayloadRow, object?> _map;
    private readonly Dictionary<string, int> _ordinals;

    internal ColumnPayload(string indexName, IReadOnlyList<string> columns, Func<LuiPayloadRow, object?> map)
    {
        Source = PayloadSource.Built(indexName);
        Columns = columns;
        _map = map;
        _ordinals = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < columns.Count; i++)
        {
            _ordinals[columns[i]] = FirstColumn + i;
        }
    }

    internal override IReadOnlyList<string> Columns { get; }

    internal override PayloadSource Source { get; }

    internal override object? Read(SqliteDataReader row)
    {
        var payloadRow = new LuiPayloadRow(row, _ordinals);
        try
        {
            return _map(payloadRow);
        }
        finally
        {
            // the reader moves on to the next hit: a row that was kept must not read it (review of 2026-11-04, A.7)
            payloadRow.Invalidate();
        }
    }
}
