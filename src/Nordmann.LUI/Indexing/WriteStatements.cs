using Microsoft.Data.Sqlite;

namespace Nordmann.LUI.Indexing;

/// <summary>
/// The write statements of one index, prepared once on one connection and
/// used again and again.
/// <para>
/// Preparing the upsert costs more than running it: SQLite compiles the
/// generated columns, the indexes and the three triggers into the statement
/// (measured: 184 µs per entry through a fresh command, 59 µs through a
/// prepared one). A connection is used by one thread at a time, so the
/// statements are guarded by the lock of their connection: the statements of
/// every index that shares a connection share its lock.
/// </para>
/// </summary>
internal sealed class WriteStatements(SqliteConnection connection, OwnedSql sql, Lock gate)
{
    private Prepared? _upsert;
    private Prepared? _updateWeight;
    private Prepared? _updatePriority;
    private Prepared? _remove;

    internal void Upsert(string key, string text, int weight, int priority, byte[]? payload, SqliteTransaction? transaction)
    {
        lock (gate)
        {
            _upsert ??= new Prepared(connection, sql.Upsert, "$key", "$text", "$weight", "$priority", "$payload");
            _upsert.Set(0, key);
            _upsert.Set(1, text);
            _upsert.Set(2, weight);
            _upsert.Set(3, priority);
            _upsert.Set(4, payload);
            _upsert.Run(transaction);
        }
    }

    internal bool UpdateWeight(string key, int weight, SqliteTransaction? transaction)
    {
        lock (gate)
        {
            _updateWeight ??= new Prepared(connection, sql.UpdateWeight, "$key", "$weight");
            _updateWeight.Set(0, key);
            _updateWeight.Set(1, weight);
            return _updateWeight.Run(transaction) > 0;
        }
    }

    internal bool UpdatePriority(string key, int priority, SqliteTransaction? transaction)
    {
        lock (gate)
        {
            _updatePriority ??= new Prepared(connection, sql.UpdatePriority, "$key", "$priority");
            _updatePriority.Set(0, key);
            _updatePriority.Set(1, priority);
            return _updatePriority.Run(transaction) > 0;
        }
    }

    internal bool Remove(string key, SqliteTransaction? transaction)
    {
        lock (gate)
        {
            _remove ??= new Prepared(connection, sql.Remove, "$key");
            _remove.Set(0, key);
            return _remove.Run(transaction) > 0;
        }
    }

    private sealed class Prepared
    {
        private readonly SqliteCommand _command;
        private readonly SqliteParameter[] _parameters;

        internal Prepared(SqliteConnection connection, string text, params string[] names)
        {
            _command = connection.CreateCommand();
            _command.CommandText = text;
            _parameters = new SqliteParameter[names.Length];

            for (var i = 0; i < names.Length; i++)
            {
                _parameters[i] = _command.Parameters.Add(names[i], SqliteType.Text);
            }
        }

        internal void Set(int index, string value) => _parameters[index].Value = value;

        internal void Set(int index, int value) => _parameters[index].Value = value;

        internal void Set(int index, byte[]? value) => _parameters[index].Value = (object?)value ?? DBNull.Value;

        internal int Run(SqliteTransaction? transaction)
        {
            _command.Transaction = transaction;
            return _command.ExecuteNonQuery();
        }
    }
}
