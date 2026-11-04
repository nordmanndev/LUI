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

        var builder = Catalog.Index("bench");
        var described = kind == CorpusKind.MultiWord ? builder.MultiWord() : builder.SingleWord();
        Index = (typoTolerance ? described.TypoTolerance() : described).Ensure();
        Index.Sync(Corpus.Entries(), new LuiSyncOptions { BatchSize = 100_000 });
    }

    internal string CorpusName { get; }

    internal Corpus Corpus { get; }

    internal LuiCatalog Catalog { get; }

    internal ILuiEmbeddedIndex<long, LuiNoPayload> Index { get; }

    public void Dispose()
    {
        Catalog.Dispose();
        _database.Dispose();
    }
}
