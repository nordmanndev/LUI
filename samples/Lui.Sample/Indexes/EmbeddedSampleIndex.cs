using System.Text.Json;
using Nordmann.LUI.Sample.Api;

namespace Nordmann.LUI.Sample.Indexes;

/// <summary>
/// An <c>Embedded</c> index (LUI-Spezifikation §5): the entries live in LUI's own table and the page writes through the
/// library. The seed is the "source of truth" the sample syncs from; it stands for the table of another database, or a
/// file, that an application would sync from (<c>Sync(entries)</c>, ADR-0016).
/// </summary>
internal sealed class EmbeddedSampleIndex(
    ILuiEmbeddedIndex index,
    Func<IReadOnlyList<LuiEntry>> seed,
    SamplePayload? payload,
    string payloadDescription,
    string description) : IEmbeddedSampleIndex
{
    public string Name => index.Name;

    public IndexInfo Describe() => new(
        index.Name,
        index.Kind.ToString(),
        "Embedded",
        index.TypoDistance.ToString(),
        index.TypoDictionaryState.ToString(),
        index.Count(),
        payloadDescription,
        description,
        Pending: null,
        Triggers: null);

    public SuggestResponse Suggest(string text, LuiQueryOptions options, CancellationToken cancellationToken) =>
        SuggestMapper.Run(index, text, options, hit => payload?.Show(hit), cancellationToken);

    public long Upsert(UpsertRequest request)
    {
        var key = request.Key ?? Random.Shared.NextInt64(1_000_000, 1_000_000_000_000);
        object? stored = null;
        if (request.Payload is { ValueKind: not (JsonValueKind.Null or JsonValueKind.Undefined) } json)
        {
            stored = payload is null
                ? throw new ArgumentException($"The index '{index.Name}' stores no payload.", nameof(request))
                : payload.Read(json);
        }

        index.Upsert(new LuiEntry(key, request.Text) { Weight = request.Weight, Priority = request.Priority, Payload = stored });
        return key;
    }

    public bool Patch(long key, PatchRequest request)
    {
        var found = true;
        if (request.Weight is { } weight)
        {
            found &= index.UpdateWeight(key, weight);
        }

        if (request.Priority is { } priority)
        {
            found &= index.UpdatePriority(key, priority);
        }

        return found;
    }

    public bool Remove(long key) => index.Remove(key);

    public LuiIntegrityReport Check() => index.Check();

    public LuiIntegrityReport CheckAgainstSeed(CancellationToken cancellationToken) => index.Check(seed(), cancellationToken);

    public void Rebuild() => index.Rebuild();

    public void Optimize() => index.Optimize();

    public void WarmUpTypoDictionary(CancellationToken cancellationToken) => index.WarmUpTypoDictionary(cancellationToken);

    public void RebuildTypoDictionary(CancellationToken cancellationToken) => index.RebuildTypoDictionary(cancellationToken);

    public object Sync(CancellationToken cancellationToken) => index.Sync(seed(), options: null, cancellationToken);
}
