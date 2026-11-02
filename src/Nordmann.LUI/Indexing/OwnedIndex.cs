using System.Runtime.CompilerServices;
using Microsoft.Data.Sqlite;
using Nordmann.LUI.Payloads;
using Nordmann.LUI.Querying;
using Nordmann.LUI.Similarity;

namespace Nordmann.LUI.Indexing;

/// <summary>
/// An index whose content lives in a table of the library (LUI-Spezifikation
/// §5). The writes are plain statements: the generated columns compute the
/// forms, the triggers keep the search index in step (E3).
/// </summary>
internal sealed class OwnedIndex<TKey, TPayload> : ILuiIndex<TKey, TPayload>
{
    private readonly LuiCatalog _catalog;
    private readonly IPayloadSerializer<TPayload>? _payload;
    private readonly QueryRunner<TKey, TPayload> _runner;
    private readonly OwnedSql _sql;

    // The statements of the application's own connections, one set per connection.
    private readonly ConditionalWeakTable<SqliteConnection, WriteStatements> _applicationStatements = [];
    private readonly Lock _ownGate = new();
    private WriteStatements? _ownStatements;

    internal OwnedIndex(
        LuiCatalog catalog,
        string name,
        IndexDefinition definition,
        IPayloadSerializer<TPayload>? payload,
        ITextSimilarity similarity)
    {
        _catalog = catalog;
        _payload = payload;
        Name = name;
        Kind = definition.Kind;
        _sql = new OwnedSql(name);
        _runner = new QueryRunner<TKey, TPayload>(catalog, name, definition, payload, similarity);
    }

    public string Name { get; }

    public IndexKind Kind { get; }

    public void Upsert(
        TKey key,
        string text,
        int weight,
        TPayload? payload = default,
        Priority priority = Priority.Normal,
        SqliteTransaction? transaction = null)
    {
        var keyText = KeyCodec<TKey>.ToText(key);
        ArgumentException.ThrowIfNullOrEmpty(text);
        RowId.ValidateWeight(weight);
        RowId.ValidatePriority(priority);

        byte[]? bytes = null;
        if (payload is not null)
        {
            bytes = _payload?.Serialize(payload)
                ?? throw new InvalidOperationException(
                    $"The index '{Name}' stores no payload: configure Payload(…) when the index is ensured.");
        }

        using var scope = _catalog.Begin();
        StatementsFor(transaction).Upsert(keyText, text, weight, (int)priority, bytes, transaction);
    }

    public bool UpdateWeight(TKey key, int weight, SqliteTransaction? transaction = null)
    {
        var keyText = KeyCodec<TKey>.ToText(key);
        RowId.ValidateWeight(weight);

        using var scope = _catalog.Begin();
        return StatementsFor(transaction).UpdateWeight(keyText, weight, transaction);
    }

    public bool UpdatePriority(TKey key, Priority priority, SqliteTransaction? transaction = null)
    {
        var keyText = KeyCodec<TKey>.ToText(key);
        RowId.ValidatePriority(priority);

        using var scope = _catalog.Begin();
        return StatementsFor(transaction).UpdatePriority(keyText, (int)priority, transaction);
    }

    public bool Remove(TKey key, SqliteTransaction? transaction = null)
    {
        var keyText = KeyCodec<TKey>.ToText(key);

        using var scope = _catalog.Begin();
        return StatementsFor(transaction).Remove(keyText, transaction);
    }

    public long Count()
    {
        using var scope = _catalog.Begin();
        using var connection = _catalog.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = _sql.Count;
        return (long)command.ExecuteScalar()!;
    }

    public LuiQuery<TKey, TPayload> Query(string text) => new(this, text);

    public LuiResult<TKey, TPayload> Query(
        string text,
        LuiQueryOptions options,
        CancellationToken cancellationToken = default) =>
        _runner.Execute(text, options, cancellationToken);

    /// <summary>
    /// The statements for a write: those of the application's connection when
    /// it brought a transaction, otherwise those of the catalog's own writer
    /// (one writer per file anyway, §15).
    /// </summary>
    private WriteStatements StatementsFor(SqliteTransaction? transaction)
    {
        if (transaction is null)
        {
            return Volatile.Read(ref _ownStatements) ?? CreateOwnStatements();
        }

        var connection = transaction.Connection
            ?? throw new ArgumentException("The transaction is complete.", nameof(transaction));

        return _applicationStatements.TryGetValue(connection, out var statements)
            ? statements
            : _applicationStatements.GetValue(connection, c => new WriteStatements(c, _sql, new Lock()));
    }

    private WriteStatements CreateOwnStatements()
    {
        lock (_ownGate)
        {
            return _ownStatements ??= new WriteStatements(_catalog.Writer, _sql, _catalog.WriterGate);
        }
    }
}
