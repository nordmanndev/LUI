using Microsoft.Data.Sqlite;

namespace Nordmann.LUI.Storage;

/// <summary>A connection that counts the commands it creates (<see cref="CommandCounter"/>).</summary>
internal sealed class CountingConnection(string connectionString, CommandCounter counter) : SqliteConnection(connectionString)
{
    /// <summary>Creates the command and counts it.</summary>
    /// <returns>The command.</returns>
    public override SqliteCommand CreateCommand()
    {
        counter.Increment();
        return base.CreateCommand();
    }
}
