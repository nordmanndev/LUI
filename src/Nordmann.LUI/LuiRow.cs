using Microsoft.Data.Sqlite;

namespace Nordmann.LUI;

/// <summary>
/// The payload columns of one hit of a <c>Linked</c> index, read from the application's table (LUI-Spezifikation
/// §12.3): what the mapping of <c>WithPayload(columns, row =&gt; …)</c> builds the payload from. It is valid only
/// while the mapping runs — do not keep it.
/// </summary>
public sealed class LuiRow
{
    private readonly SqliteDataReader _reader;
    private readonly IReadOnlyDictionary<string, int> _ordinals;

    internal LuiRow(SqliteDataReader reader, IReadOnlyDictionary<string, int> ordinals)
    {
        _reader = reader;
        _ordinals = ordinals;
    }

    /// <summary>Whether the column is <c>NULL</c>.</summary>
    /// <param name="column">A column named in <c>WithPayload</c>.</param>
    /// <returns><see langword="true"/> for <c>NULL</c>.</returns>
    public bool IsNull(string column) => _reader.IsDBNull(Ordinal(column));

    /// <summary>The column as text.</summary>
    /// <param name="column">A column named in <c>WithPayload</c>.</param>
    /// <returns>The value.</returns>
    public string GetString(string column) => _reader.GetString(Ordinal(column));

    /// <summary>The column as an integer.</summary>
    /// <param name="column">A column named in <c>WithPayload</c>.</param>
    /// <returns>The value.</returns>
    public long GetInt64(string column) => _reader.GetInt64(Ordinal(column));

    /// <summary>The column as a floating-point number.</summary>
    /// <param name="column">A column named in <c>WithPayload</c>.</param>
    /// <returns>The value.</returns>
    public double GetDouble(string column) => _reader.GetDouble(Ordinal(column));

    /// <summary>The column as <typeparamref name="T"/>, converted by <c>Microsoft.Data.Sqlite</c>.</summary>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <param name="column">A column named in <c>WithPayload</c>.</param>
    /// <returns>The value.</returns>
    public T Get<T>(string column) => _reader.GetFieldValue<T>(Ordinal(column));

    private int Ordinal(string column) =>
        _ordinals.TryGetValue(column, out var ordinal)
            ? ordinal
            : throw new ArgumentException(
                $"'{column}' is not a payload column of this index: name it in WithPayload(columns, …).", nameof(column));
}
