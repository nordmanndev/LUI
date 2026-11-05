namespace Nordmann.LUI.Sample.Data;

/// <summary>
/// The <c>words</c> index: every word of the German word list, one word per entry. It is the stock of short texts LUI is built
/// for — 685,829 words, downloaded by the sample itself when the index is filled.
/// </summary>
internal sealed class WordSource : IBulkSource
{
    private GermanWordList? _list;

    public WordSource(string? configuredPath)
    {
        // The list that is on the machine is known before the fill; the one that is not there is downloaded by the fill.
        _list = GermanWordList.Find(configuredPath);
    }

    public string Name => "words";

    public LuiIndexKind Kind => LuiIndexKind.SingleWord;

    public bool TypoTolerance => false;

    public string Description =>
        "Every word of the German word list, one word per entry — the stock of short texts LUI is built for. " +
        "Fill it in the index panel below; the query stays in the milliseconds while the index grows.";

    public string Origin => _list?.Origin ?? $"the German word list — {GermanWordList.Size:N0} words (downloaded when the index is filled)";

    public long? Capacity => _list?.Count ?? GermanWordList.Size;

    public async Task PrepareAsync(Action<string>? note, CancellationToken cancellationToken)
    {
        _list ??= await GermanWordList.DownloadAsync(note, cancellationToken).ConfigureAwait(false);
    }

    public IEnumerable<LuiEntry<long, LuiNoPayload>> Entries(int count)
    {
        var list = _list ?? throw new InvalidOperationException("The word list is not ready yet.");

        return list.Words(count).Select((word, i) => new LuiEntry<long, LuiNoPayload>(i + 1, word) { Weight = BulkWeights.Of(word) });
    }
}
