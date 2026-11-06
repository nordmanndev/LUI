using Nordmann.LUI.Sample.Api;
using Nordmann.LUI.Sample.Data;

namespace Nordmann.LUI.Sample.Indexes;

/// <summary>
/// An embedded index whose source is a function of the number of entries (ADR-0029). Everything an index can do — the query,
/// the check, the maintenance — it does as an ordinary embedded index; what is its own is the source: the seed of the other
/// indexes is a list that is checked in, this one is what the user asks for.
/// <para>
/// The index of LUI is created when it is asked for the first time — not when the host starts: a stock of a million entries
/// is minutes of work, and nobody pays for it who does not want it.
/// </para>
/// </summary>
internal sealed class BulkSampleIndex(LuiCatalog catalog, IBulkSource source) : IBulkSampleIndex
{
    /// <summary>How many entries one batch holds — one commit, one progress report.</summary>
    private const int BatchSize = 100_000;

    private readonly Lock _gate = new();
    private ILuiEmbeddedIndex<long, LuiNoPayload>? _index;

    public string Name => source.Name;

    public long? Capacity => source.Capacity;

    /// <summary>The index of LUI — created when it is asked for the first time.</summary>
    private ILuiEmbeddedIndex<long, LuiNoPayload> Index
    {
        get
        {
            lock (_gate)
            {
                return _index ??= Create();
            }
        }
    }

    public IndexInfo Describe() => new(
        Index.Name,
        Index.Kind.ToString(),
        "Embedded",
        Index.TypoTolerance,
        Index.TypoDictionaryState.ToString(),
        Index.Count(),
        Payload: "none",
        Description: source.Description,
        Pending: null,
        Triggers: null,
        Source: source.Origin,
        Capacity: source.Capacity);

    public SuggestResponse Suggest(string text, LuiQueryOptions options, CancellationToken cancellationToken) =>
        SuggestMapper.Run(Index, text, options, cancellationToken);

    public LuiIntegrityReport Check() => Index.Check();

    public LuiIntegrityReport CheckAgainstSeed(CancellationToken cancellationToken) => Index.Check(Entries(CurrentCount()), cancellationToken);

    public void Optimize() => Index.Optimize();

    public void WarmUpTypoDictionary(CancellationToken cancellationToken) => Index.WarmUpTypoDictionary(cancellationToken);

    public void RebuildTypoDictionary(CancellationToken cancellationToken) => Index.RebuildTypoDictionary(cancellationToken);

    /// <summary>Brings the index in step with its source — which is what it holds already, so nothing changes.</summary>
    public object Sync(CancellationToken cancellationToken) => Index.Sync(Entries(CurrentCount()), options: null, cancellationToken);

    public async Task<LuiSyncReport> Fill(int count, IProgress<LuiSyncReport>? progress, Action<string>? note, CancellationToken cancellationToken)
    {
        await source.PrepareAsync(note, cancellationToken).ConfigureAwait(false);

        // A big batch: a first load into a file that no one else writes is about 30 % faster with 100,000 entries than
        // with the default of 10,000 (ADR-0016) — and the page hears the progress after every batch.
        return Index.Sync(Entries(count), new LuiSyncOptions { BatchSize = BatchSize, Progress = progress }, cancellationToken);
    }

    /// <summary>Creates the index of LUI: one word or several, with the typo tolerance the source asks for.</summary>
    private ILuiEmbeddedIndex<long, LuiNoPayload> Create()
    {
        if (source.Kind == LuiIndexKind.MultiWord)
        {
            return source.TypoTolerance
                ? catalog.Create(source.Name, index => index.MultiWord().TypoTolerance())
                : catalog.Create(source.Name, index => index.MultiWord());
        }

        return source.TypoTolerance
            ? catalog.Create(source.Name, index => index.SingleWord().TypoTolerance())
            : catalog.Create(source.Name, index => index.SingleWord());
    }

    /// <summary>The entries of the source for the number the index holds.</summary>
    private IEnumerable<LuiEntry<long, LuiNoPayload>> Entries(int count) => source.Entries(count);

    private int CurrentCount() => (int)Math.Min(Index.Count(), int.MaxValue);
}
