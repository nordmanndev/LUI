using Microsoft.Data.Sqlite;

namespace Nordmann.LUI.Storage;

/// <summary>
/// What an operation of an index asks before its first statement (ADR-0075): is the definition I was built from still the
/// one in the file? The layers below the indexes — the query path — know only this question, not the stored definitions.
/// </summary>
internal interface IConnectionGuard
{
    /// <summary>
    /// Checks the index on the connection of the operation: one statement, the SQLite schema counter, and a reload of the
    /// definitions only when the counter changed.
    /// </summary>
    /// <param name="connection">The connection of the operation.</param>
    /// <param name="transaction">Its transaction, when it has one — a command of a connection in a transaction needs it.</param>
    /// <exception cref="LuiIndexNotFoundException">The index was dropped.</exception>
    /// <exception cref="LuiDefinitionMismatchException">The index was created again with another definition.</exception>
    void Check(SqliteConnection connection, SqliteTransaction? transaction = null);
}
