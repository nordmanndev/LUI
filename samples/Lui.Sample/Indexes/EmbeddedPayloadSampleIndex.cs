using System.Text.Json;
using Nordmann.LUI.Sample.Api;
using Nordmann.LUI.Sample.Data;

namespace Nordmann.LUI.Sample.Indexes;

/// <summary>
/// An <c>Embedded</c> index (ADR-0014, ADR-0015) with a payload — the handle of a definition with a payload type: the entries carry the
/// payload, and so do the hits. The seed is the "source of truth" the sample syncs from (<c>Sync(entries)</c>, ADR-0016).
/// </summary>
internal sealed class EmbeddedPayloadSampleIndex(
    LuiEmbeddedIndex<long, RecipePayload> index,
    Func<IReadOnlyList<LuiEntry<long, RecipePayload>>> seed,
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
        SuggestMapper.Run(index, text, options, cancellationToken);

    public long Upsert(UpsertRequest request)
    {
        var key = request.Key ?? Random.Shared.NextInt64(1_000_000, 1_000_000_000_000);
        if (request.Payload is not { ValueKind: not (JsonValueKind.Null or JsonValueKind.Undefined) } json)
        {
            throw new ArgumentException($"The index '{index.Name}' stores a payload with every entry.", nameof(request));
        }

        var payload = json.Deserialize(SampleJsonContext.Default.RecipePayload)!;
        index.Upsert(new LuiEntry<long, RecipePayload>(key, request.Text, payload) { Weight = request.Weight, Priority = request.Priority });
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
