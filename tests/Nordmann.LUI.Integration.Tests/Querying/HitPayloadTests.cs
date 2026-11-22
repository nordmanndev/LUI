using Shouldly;

namespace Nordmann.LUI.Integration.Tests.Querying;

/// <summary>
/// The payload of a hit (ADR-0074, issue #262): a query on an index with payload loads the payloads of its hits with the query,
/// and a query on an index without one reads none.
/// </summary>
public sealed class HitPayloadTests
{
    private static readonly LuiIndexDefinition<long> Plain = new("plain", index => index.SingleWord());

    private static readonly LuiIndexDefinition<long> Bare = new("bare", index => index.SingleWord());

    [Fact]
    public void A_query_without_payload_allocates_nothing_for_payloads()
    {
        // the old calls could store a payload in an index that has no declared payload type; a typed handle without a payload type
        // does not read it — the statement of its hits has no payload column, so the bytes are never copied (ADR-0074)
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var plain = catalog.Create("plain", index => index.SingleWord());
        var bare = catalog.Create("bare", index => index.SingleWord());
        var big = new Chip(new string('x', 4_000));
        for (var key = 1; key <= 60; key++)
        {
            plain.Upsert(new LuiEntry(key, $"apfel{key}") { Weight = key, Payload = big });
            bare.Upsert(new LuiEntry(key, $"apfel{key}") { Weight = key });
        }

        var withStoredPayloads = catalog.Index(Plain);
        var withoutPayloads = catalog.Index(Bare);

        var (stored, none) = OnOwnThread(() => (Measure(withStoredPayloads), Measure(withoutPayloads)));

        // twenty hits of 4 KB each would be 80 KB more: the query of an index without payload type does not carry the bytes
        (stored - none).ShouldBeLessThan(
            2_048,
            $"A query on an index whose entries store 4 KB payloads allocates {stored / 1024.0:F1} KB, one without payloads {none / 1024.0:F1} KB: "
            + "a handle without a payload type must not read the payload column.");
    }

    /// <summary>Runs the action on a thread of its own: the pool of the buffers of the candidates is thread-static.</summary>
    private static (long, long) OnOwnThread(Func<(long, long)> action)
    {
        (long, long) result = default;
        var thread = new Thread(() => result = action());
        thread.Start();
        thread.Join();
        return result;
    }

    /// <summary>The allocation of one query of twenty hits, after a warm-up query with the same options.</summary>
    private static long Measure(LuiIndex<long> index)
    {
        var options = new LuiQueryOptions { MaxResults = 20, TypoTolerance = false };
        index.Search("apfel", options);

        var before = GC.GetAllocatedBytesForCurrentThread();
        index.Search("apfel", options);
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }
}
