using Microsoft.Data.Sqlite;
using Nordmann.LUI.Schema;

namespace Nordmann.LUI.Querying;

/// <summary>
/// A payload built from columns of the application's table (<c>Linked</c>, ADR-0035): the columns
/// are selected with the hits, in this order, and the mapping turns them into the payload. A map that throws fails the query with
/// a <see cref="LuiPayloadException"/> that names the index, the key and the type it builds (ADR-0074).
/// </summary>
internal sealed class ColumnPayload : PayloadReader
{
    /// <summary>The column of the key in the row of a hit: after <c>doc_id</c>.</summary>
    private const int KeyColumn = 1;

    private readonly string _indexName;
    private readonly LuiKeyType _keyType;
    private readonly Type _payloadType;
    private readonly Func<LuiPayloadRow, object?> _map;
    private readonly Dictionary<string, int> _ordinals;

    internal ColumnPayload(string indexName, string keyType, Type payloadType, IReadOnlyList<string> columns, Func<LuiPayloadRow, object?> map)
    {
        _indexName = indexName;
        _keyType = KeyCodec.TypeOf(keyType);
        _payloadType = payloadType;
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
        catch (Exception exception) when (exception is not (OperationCanceledException or LuiPayloadException))
        {
            // the key is read only now, for the message: a map that does not throw costs nothing more than before
            throw new LuiPayloadException(
                _indexName,
                KeyCodec.FromText(row.GetString(KeyColumn), _keyType, _indexName),
                _payloadType,
                $"the map of the linked index threw: {exception.Message}",
                exception);
        }
        finally
        {
            // the reader moves on to the next hit: a row that was kept must not read it (review of 2026-11-04, A.7)
            payloadRow.Invalidate();
        }
    }
}
