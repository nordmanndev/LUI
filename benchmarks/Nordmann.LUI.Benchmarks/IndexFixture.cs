using Nordmann.LUI.Testing;

namespace Nordmann.LUI.Benchmarks;

/// <summary>
/// An index of a generated corpus in a database file of its own, built once per benchmark class by the bulk path of
/// <c>Sync</c> — the corpus of the budget tests (ADR-0020), so the benchmark and the budget measure the same thing.
/// </summary>
internal sealed class IndexFixture : IDisposable
{
    private readonly TestDatabase _database = new();

    internal IndexFixture(CorpusKind kind, int entries, bool typoTolerance = false)
    {
        var words = TestCorpus.MeasurementWords(out var corpusName);
        CorpusName = corpusName;
        Corpus = Corpus.Generate(words, entries, kind);
        Catalog = _database.Open(new LuiCatalogOptions { CacheSizeMegabytes = 512, MemoryMapMegabytes = 2048 });

        Index = Create(kind, typoTolerance);
        Index.Sync(Corpus.Entries(), new LuiSyncOptions { BatchSize = 100_000 });
    }

    /// <summary>Creates the index: one word or several, with the typo tolerance the caller asks for.</summary>
    private ILuiEmbeddedIndex Create(CorpusKind kind, bool typoTolerance)
    {
        if (kind == CorpusKind.MultiWord)
        {
            return typoTolerance
                ? Catalog.Create("bench", index => index.MultiWord().TypoTolerance())
                : Catalog.Create("bench", index => index.MultiWord());
        }

        return typoTolerance
            ? Catalog.Create("bench", index => index.SingleWord().TypoTolerance())
            : Catalog.Create("bench", index => index.SingleWord());
    }

    internal string CorpusName { get; }

    internal Corpus Corpus { get; }

    internal LuiCatalog Catalog { get; }

    internal ILuiEmbeddedIndex Index { get; }

    public void Dispose()
    {
        Catalog.Dispose();
        _database.Dispose();
    }
}
