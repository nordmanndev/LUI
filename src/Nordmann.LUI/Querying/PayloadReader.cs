using Microsoft.Data.Sqlite;

namespace Nordmann.LUI.Querying;

/// <summary>
/// Reads the payload of a hit from the row of the hits statement (<c>ContentSource.HitsSql</c>), starting at
/// the column after the key. Where the payload comes from is a matter of the content source: bytes of a
/// serialiser for <c>Owned</c>, columns of the application's table for <c>Linked</c> (PR-006).
/// </summary>
/// <typeparam name="TPayload">The payload type.</typeparam>
internal abstract class PayloadReader<TPayload>
{
    /// <summary>The column of the hits statement where the payload begins.</summary>
    internal const int FirstColumn = 2;

    /// <summary>The payload of the current row; the default when there is none.</summary>
    internal abstract TPayload? Read(SqliteDataReader row);
}
