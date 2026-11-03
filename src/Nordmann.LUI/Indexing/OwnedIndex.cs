using System.Runtime.CompilerServices;
using Microsoft.Data.Sqlite;
using Nordmann.LUI.Normalization;
using Nordmann.LUI.Payloads;
using Nordmann.LUI.Querying;
using Nordmann.LUI.Similarity;
using Nordmann.LUI.Typos;

namespace Nordmann.LUI.Indexing;

/// <summary>
/// An index whose content lives in a table of the library (LUI-Spezifikation
/// §5). The writes are plain statements: the generated columns compute the
/// forms, the triggers keep the search index in step (E3).
/// </summary>
#pragma warning disable CA1001 // see TypoDictionary: the lock of the dictionary needs no disposal, and an index lives as long as its catalog
internal sealed class OwnedIndex<TKey, TPayload> : ILuiIndex<TKey, TPayload>
#pragma warning restore CA1001
{
    private readonly LuiCatalog _catalog;
    private readonly IPayloadSerializer<TPayload>? _payload;
    private readonly QueryRunner<TKey, TPayload> _runner;
    /// <summary><c>SQLITE_CONSTRAINT_CHECK</c>.</summary>
    private const int SqliteCheckConstraintFailed = 275;

    private readonly OwnedSql _sql;
    private readonly TypoDictionary _typos;

    // The statements of the application's own connections, one set per connection.
    private readonly ConditionalWeakTable<SqliteConnection, WriteStatements> _applicationStatements = [];
    private readonly Lock _ownGate = new();
    private WriteStatements? _ownStatements;

    internal OwnedIndex(
        LuiCatalog catalog,
        string name,
        IndexDefinition definition,
        IPayloadSerializer<TPayload>? payload,
        ITextSimilarity similarity,
        bool typoTolerance)
    {
        _catalog = catalog;
        _payload = payload;
        Name = name;
        Kind = definition.Kind;
        _sql = new OwnedSql(name);
        _typos = new TypoDictionary(
            new NormalizationOptions { GermanTransliteration = definition.GermanTransliteration },
            () => ReadTexts(name));
        _runner = new QueryRunner<TKey, TPayload>(catalog, name, definition, payload, similarity, _typos, typoTolerance);
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

        // A payload that is null, or the default of a value type, is "no payload" — it is stored as NULL
        // and read back as the default. A value type cannot say more (review B1).
        byte[]? bytes = null;
        if (payload is not null && !EqualityComparer<TPayload>.Default.Equals(payload, default))
        {
            bytes = _payload?.Serialize(payload)
                ?? throw new InvalidOperationException(
                    $"The index '{Name}' stores no payload: configure Payload(…) when the index is ensured.");
        }

        using var scope = _catalog.Begin();
        try
        {
            StatementsFor(transaction).Upsert(keyText, text, weight, (int)priority, bytes, transaction);

            // the new word beginnings join the typo dictionary, if it is built (§10.1)
            _typos.Add(text, weight);
        }
        catch (SqliteException exception) when (IsDocIdExhausted(exception))
        {
            throw new InvalidOperationException(
                $"The index '{Name}' has used up its document ids (0 … {RowId.MaxDocId:N0}); a new entry gets the "
                + "highest id plus one. Export the entries, Drop() the index, ensure it again and load them: "
                + "the ids start from 1 again.",
                exception);
        }
    }

    private static bool IsDocIdExhausted(SqliteException exception) =>
        exception.SqliteExtendedErrorCode == SqliteCheckConstraintFailed
        && exception.Message.Contains("doc_id", StringComparison.Ordinal);

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

    public void RebuildTypoDictionary()
    {
        using var scope = _catalog.Begin();
        _typos.Rebuild();
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
            : _applicationStatements.GetValue(connection, c => ForApplication(c, transaction));
    }

    /// <summary>
    /// The statements for a connection of the application. The first use on a connection checks that
    /// the connection reaches this index at all — a transaction of another database would otherwise
    /// fail with a bare "no such table" (review B8).
    /// </summary>
    private WriteStatements ForApplication(SqliteConnection connection, SqliteTransaction transaction)
    {
        using var check = connection.CreateCommand();
        check.Transaction = transaction;
        check.CommandText = "SELECT count(*) FROM sqlite_master WHERE type = 'table' AND name = $name;";
        check.Parameters.AddWithValue("$name", OwnedSchema.Content(Name));

        if ((long)check.ExecuteScalar()! == 0)
        {
            throw new ArgumentException(
                $"The transaction belongs to another database: the table '{OwnedSchema.Content(Name)}' of the "
                + $"index '{Name}' is not there.",
                nameof(transaction));
        }

        return new WriteStatements(connection, _sql, new Lock());
    }

    /// <summary>The texts and weights of the index — the source of the typo dictionary.</summary>
    private IEnumerable<(string Text, int Weight)> ReadTexts(string name)
    {
        using var connection = _catalog.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT text, weight FROM {OwnedSchema.Content(name)};";

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            yield return (reader.GetString(0), reader.GetInt32(1));
        }
    }

    private WriteStatements CreateOwnStatements()
    {
        lock (_ownGate)
        {
            return _ownStatements ??= new WriteStatements(_catalog.Writer, _sql, _catalog.WriterGate);
        }
    }
}
