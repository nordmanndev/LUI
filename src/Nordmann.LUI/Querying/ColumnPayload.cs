using Microsoft.Data.Sqlite;

namespace Nordmann.LUI.Querying;

/// <summary>
/// A payload built from columns of the application's table (<c>Linked</c>, LUI-Spezifikation §12.3): the columns
/// are selected with the hits, in this order, and the mapping turns them into the payload.
/// </summary>
/// <typeparam name="TPayload">The payload type.</typeparam>
internal sealed class ColumnPayload<TPayload> : PayloadReader<TPayload>
{
    private readonly Func<LuiRow, TPayload> _map;
    private readonly Dictionary<string, int> _ordinals;

    internal ColumnPayload(IReadOnlyList<string> columns, Func<LuiRow, TPayload> map)
    {
        Columns = columns;
        _map = map;
        _ordinals = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < columns.Count; i++)
        {
            _ordinals[columns[i]] = FirstColumn + i;
        }
    }

    internal override IReadOnlyList<string> Columns { get; }

    internal override TPayload? Read(SqliteDataReader row) => _map(new LuiRow(row, _ordinals));
}
