using Microsoft.Data.Sqlite;
using Nordmann.LUI.Sample.Api;
using Nordmann.LUI.Sample.Data;

namespace Nordmann.LUI.Sample.Indexes;

/// <summary>
/// A <c>Linked</c> index (LUI-Spezifikation §5, ADR-0015): the text lives in the application's own table, and LUI only
/// reads it. The page writes to that table, as the application would; the index follows through the triggers the
/// application created on purpose (they note the key in LUI's outbox) and the hosted service that applies the outbox,
/// or by <c>Refresh</c> when the application tells it.
/// </summary>
internal sealed class LinkedSampleIndex<TPayload>(
    ILuiLinkedIndex<TPayload> index,
    string connectionString,
    string payloadDescription,
    string description) : ILinkedSampleIndex
{
    public string Name => index.Name;

    public IndexInfo Describe() => new(
        index.Name,
        index.Kind.ToString(),
        "Linked",
        index.TypoTolerance,
        index.TypoDictionaryState.ToString(),
        index.Count(),
        payloadDescription,
        description,
        index.Pending(),
        index.Triggers().ToString());

    public SuggestResponse Suggest(string text, LuiQueryOptions options, CancellationToken cancellationToken) =>
        SuggestMapper.Run(index, text, options, cancellationToken);

    public long UpsertRow(TagRowRequest request)
    {
        Validate(request);

        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"""
            INSERT INTO {SampleDatabase.TagTable} (id, name, usage_count, rank, kind, color)
            VALUES ($id, $name, $usage, $rank, $kind, $color)
            ON CONFLICT(id) DO UPDATE SET
                name = excluded.name, usage_count = excluded.usage_count, rank = excluded.rank,
                kind = excluded.kind, color = excluded.color
            RETURNING id
            """;
        command.Parameters.AddWithValue("$id", request.Id is { } id ? id : DBNull.Value);
        command.Parameters.AddWithValue("$name", request.Name.Trim());
        command.Parameters.AddWithValue("$usage", request.UsageCount);
        command.Parameters.AddWithValue("$rank", request.Rank);
        command.Parameters.AddWithValue("$kind", request.Kind.Trim());
        command.Parameters.AddWithValue("$color", request.Color);
        return (long)command.ExecuteScalar()!;
    }

    public bool DeleteRow(long id)
    {
        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"DELETE FROM {SampleDatabase.TagTable} WHERE id = $id";
        command.Parameters.AddWithValue("$id", id);
        return command.ExecuteNonQuery() > 0;
    }

    public int Refresh(IReadOnlyCollection<long> keys, CancellationToken cancellationToken) =>
        index.RefreshMany(keys, transaction: null, cancellationToken);

    public int ApplyPending(CancellationToken cancellationToken) => index.ApplyPending(cancellationToken);

    public void SetTriggers(bool create)
    {
        if (create)
        {
            index.CreateTriggers();
        }
        else
        {
            index.DropTriggers();
        }
    }

    public LuiIntegrityReport Check() => index.Check();

    public void Optimize() => index.Optimize();

    public void WarmUpTypoDictionary(CancellationToken cancellationToken) => index.WarmUpTypoDictionary(cancellationToken);

    public void RebuildTypoDictionary(CancellationToken cancellationToken) => index.RebuildTypoDictionary(cancellationToken);

    public object Sync(CancellationToken cancellationToken) => index.Sync(cancellationToken);

    private static void Validate(TagRowRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 80)
        {
            throw new ArgumentException("name is 1 … 80 characters.", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.Kind) || request.Kind.Trim().Length > 40)
        {
            throw new ArgumentException("kind is 1 … 40 characters.", nameof(request));
        }

        if (!IsColor(request.Color))
        {
            throw new ArgumentException("color is #rrggbb.", nameof(request));
        }

        if (request.Rank is < 0 or > 3)
        {
            throw new ArgumentException("rank is 0 (System) … 3 (Normal).", nameof(request));
        }

        if (request.UsageCount is < 0 or > 536_870_911)
        {
            throw new ArgumentException("usageCount is 0 … 536,870,911.", nameof(request));
        }
    }

    private static bool IsColor(string color) =>
        color.Length == 7 && color[0] == '#' && color.Skip(1).All(char.IsAsciiHexDigit);
}
