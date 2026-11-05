using Nordmann.LUI.Sample.Api;
using Nordmann.LUI.Sample.Data;

namespace Nordmann.LUI.Sample.Indexes;

/// <summary>
/// An embedded index whose source is a function of the number of entries (ADR-0029). Everything an index can do — the query,
/// the check, the maintenance — it does as an ordinary embedded index; what is its own is the source: the seed of the other
/// indexes is a list that is checked in, this one is what the user asks for.
/// </summary>
internal sealed class BulkSampleIndex(ILuiEmbeddedIndex<long, LuiNoPayload> index, IBulkSource source) : IBulkSampleIndex
{
    public string Name => index.Name;

    public long? Capacity => source.Capacity;

    public IndexInfo Describe() => new(
        index.Name,
        index.Kind.ToString(),
        "Embedded",
        index.TypoTolerance,
        index.TypoDictionaryState.ToString(),
        index.Count(),
        Payload: "none",
        Description: source.Description,
        Pending: null,
        Triggers: null,
        Source: source.Origin,
        Capacity: source.Capacity);

    public SuggestResponse Suggest(string text, LuiQueryOptions options, CancellationToken cancellationToken) =>
        SuggestMapper.Run(index, text, options, cancellationToken);

    public LuiIntegrityReport Check() => index.Check();

    public LuiIntegrityReport CheckAgainstSeed(CancellationToken cancellationToken) => index.Check(Entries(CurrentCount()), cancellationToken);

    public void Optimize() => index.Optimize();

    public void WarmUpTypoDictionary(CancellationToken cancellationToken) => index.WarmUpTypoDictionary(cancellationToken);

    public void RebuildTypoDictionary(CancellationToken cancellationToken) => index.RebuildTypoDictionary(cancellationToken);

    /// <summary>Brings the index in step with its source — which is what it holds already, so nothing changes.</summary>
    public object Sync(CancellationToken cancellationToken) => index.Sync(Entries(CurrentCount()), options: null, cancellationToken);

    public LuiSyncReport Fill(int count, IProgress<LuiSyncReport>? progress, CancellationToken cancellationToken) =>
        index.Sync(Entries(count), new LuiSyncOptions { Progress = progress }, cancellationToken);

    /// <summary>The entries of the source for the number the index holds.</summary>
    private IEnumerable<LuiEntry<long, LuiNoPayload>> Entries(int count) => source.Entries(count);

    private int CurrentCount() => (int)Math.Min(index.Count(), int.MaxValue);
}
