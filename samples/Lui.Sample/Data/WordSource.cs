namespace Nordmann.LUI.Sample.Data;

/// <summary>
/// The <c>words</c> index: every word of the German word list, one word per entry. It is the stock of short texts LUI is built
/// for — 685,829 words when the list has been downloaded, 7,071 when the sample falls back to the curated subset. When there is
/// no list at all, the index cannot be filled and says so.
/// </summary>
internal sealed class WordSource(GermanWordList? list) : IBulkSource
{
    public string Name => "words";

    public LuiIndexKind Kind => LuiIndexKind.SingleWord;

    public string Description =>
        "Every word of the German word list, one word per entry — the stock of short texts LUI is built for. " +
        "Fill it in the index panel below; the query stays in the milliseconds while the index grows.";

    public string Origin => list?.Origin ?? "no word list on this machine — run tools/fetch-wordlist.sh, or set LUI_WORDLIST";

    public long? Capacity => list?.Count ?? 0;

    public IEnumerable<LuiEntry<long, LuiNoPayload>> Entries(int count)
    {
        if (list is null)
        {
            throw new InvalidOperationException(
                "There is no German word list on this machine. Run tools/fetch-wordlist.sh, or set LUI_WORDLIST to a list.");
        }

        return list.Words(count).Select((word, i) => new LuiEntry<long, LuiNoPayload>(i + 1, word) { Weight = BulkWeights.Of(word) });
    }
}
