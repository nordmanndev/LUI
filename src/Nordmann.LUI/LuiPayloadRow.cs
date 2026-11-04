using Microsoft.Data.Sqlite;

namespace Nordmann.LUI;

/// <summary>
/// The payload columns of one hit of a <c>Linked</c> index, read from the application's table (LUI-Spezifikation
/// §12.3): what the mapping of <c>WithPayload(columns, row =&gt; …)</c> builds the payload from. It is valid only
/// while the mapping runs: copy the values you need out of it. A row that is kept and read after the mapping throws an
/// <see cref="InvalidOperationException"/> — it would otherwise read the next hit's row, or none, and give the wrong
/// value without a word (review of 2026-11-04, A.7).
/// </summary>
public sealed class LuiPayloadRow
{
    private SqliteDataReader? _reader;
    private readonly IReadOnlyDictionary<string, int> _ordinals;

    internal LuiPayloadRow(SqliteDataReader reader, IReadOnlyDictionary<string, int> ordinals)
    {
        _reader = reader;
        _ordinals = ordinals;
    }

    /// <summary>Whether the column is <c>NULL</c>.</summary>
    /// <param name="column">A column named in <c>WithPayload</c>.</param>
    /// <returns><see langword="true"/> for <c>NULL</c>.</returns>
    public bool IsNull(string column) => Reader.IsDBNull(Ordinal(column));

    /// <summary>The column as text.</summary>
    /// <param name="column">A column named in <c>WithPayload</c>.</param>
    /// <returns>The value.</returns>
    public string GetString(string column) => Reader.GetString(Ordinal(column));

    /// <summary>The column as an integer.</summary>
    /// <param name="column">A column named in <c>WithPayload</c>.</param>
    /// <returns>The value.</returns>
    public long GetInt64(string column) => Reader.GetInt64(Ordinal(column));

    /// <summary>The column as a floating-point number.</summary>
    /// <param name="column">A column named in <c>WithPayload</c>.</param>
    /// <returns>The value.</returns>
    public double GetDouble(string column) => Reader.GetDouble(Ordinal(column));

    /// <summary>The column as <typeparamref name="T"/>, converted by <c>Microsoft.Data.Sqlite</c>.</summary>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <param name="column">A column named in <c>WithPayload</c>.</param>
    /// <returns>The value.</returns>
    public T Get<T>(string column) => Reader.GetFieldValue<T>(Ordinal(column));

    /// <summary>Ends the validity of the row: the mapping is done, the reader goes on to the next hit.</summary>
    internal void Invalidate() => _reader = null;

    private SqliteDataReader Reader =>
        _reader ?? throw new InvalidOperationException(
            "A LuiPayloadRow is valid only while the mapping of WithPayload runs: copy the values you need out of it, "
            + "do not keep the row.");

    private int Ordinal(string column) =>
        _ordinals.TryGetValue(column, out var ordinal)
            ? ordinal
            : throw new ArgumentException(
                $"'{column}' is not a payload column of this index: name it in WithPayload(columns, …).", nameof(column));
}
