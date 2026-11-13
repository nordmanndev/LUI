using Microsoft.Data.Sqlite;
using Nordmann.LUI.Testing;

namespace Nordmann.LUI.Benchmarks;

/// <summary>
/// An index of a corpus of the repository in a database file of its own, built once per benchmark class by the bulk path
/// of <c>Sync</c> — the corpus of the budget tests (ADR-0020, ADR-0059), so the benchmark and the budget measure the
/// same thing. The corpus is a <b>file</b>: the benchmark does not build a corpus of its own, and a corpus that is
/// generated is a different corpus on every run.
/// <para>
/// The fixture knows two sources: <c>Embedded</c>, whose content lives in the index itself, and <c>Linked</c>, whose
/// content lives in a table of the application (ADR-0015). The linked source joins the embedded one here — one fixture,
/// one report, both kinds — and the report names the source of every line.
/// </para>
/// </summary>
internal sealed class IndexFixture : IDisposable
{
    /// <summary>
    /// The fixtures of the process, by their key. The cases of a benchmark class run in one process, and a case that
    /// builds the index of its predecessor again pays for the build twice — the build is the expensive part, not the query.
    /// </summary>
    private static readonly Dictionary<string, IndexFixture> Shared = [];

    private readonly TestDatabase _database = new();
    private readonly LuiCatalog _catalog;

    internal IndexFixture(string corpusName, int entries, string source = QuerySettings.Embedded)
    {
        CorpusName = corpusName;
        Source = source;
        Corpus = Corpus.From(CorpusFile.Open(QuerySettings.FileOf(corpusName), entries, 0));
        _catalog = _database.Open(new LuiCatalogOptions { CacheSizeMegabytes = 512, MemoryMapMegabytes = 2048 });

        Index = string.Equals(source, QuerySettings.Linked, StringComparison.Ordinal)
            ? CreateLinked(Corpus)
            : CreateEmbedded(Corpus);
    }

    /// <summary>The fixture of a configuration, shared by the cases that need the same one.</summary>
    /// <param name="corpusName">The corpus file.</param>
    /// <param name="entries">How many entries of the corpus the index holds.</param>
    /// <param name="source">Where the content lives.</param>
    /// <returns>The fixture.</returns>
    internal static IndexFixture Of(string corpusName, int entries, string source)
    {
        var key = $"{corpusName}|{entries}|{source}";
        lock (Shared)
        {
            if (Shared.TryGetValue(key, out var fixture))
            {
                return fixture;
            }

            fixture = new IndexFixture(corpusName, entries, source);
            Shared[key] = fixture;
            return fixture;
        }
    }

    /// <summary>An embedded index: the content lives in the index itself.</summary>
    private ILuiIndex CreateEmbedded(Corpus corpus)
    {
        var index = corpus.Kind == CorpusKind.MultiWord
            ? _catalog.Create("bench", builder => builder.MultiWord())
            : _catalog.Create("bench", builder => builder.SingleWord());
        index.Sync(corpus.Entries(), new LuiSyncOptions { BatchSize = 100_000 });
        return index;
    }

    /// <summary>A linked index: the content lives in a table of the application, and the index follows it.</summary>
    private ILuiIndex CreateLinked(Corpus corpus)
    {
        CreateTable(corpus);
        var index = corpus.Kind == CorpusKind.MultiWord
            ? _catalog.CreateLinked("bench", builder => builder.MultiWord().LinkTo("tag", "id", "name", weight: "usage_count"))
            : _catalog.CreateLinked("bench", builder => builder.SingleWord().LinkTo("tag", "id", "name", weight: "usage_count"));
        index.Sync();
        return index;
    }

    /// <summary>The table of the application: the entries of the corpus, one row each.</summary>
    private void CreateTable(Corpus corpus)
    {
        using var connection = _database.Connect();
        using (var create = connection.CreateCommand())
        {
            create.CommandText = "CREATE TABLE tag (id INTEGER PRIMARY KEY, name TEXT NOT NULL, usage_count INTEGER NOT NULL);";
            create.ExecuteNonQuery();
        }

        using var transaction = connection.BeginTransaction();
        using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = "INSERT INTO tag VALUES ($id, $name, $weight);";
        var id = insert.Parameters.Add("$id", SqliteType.Integer);
        var name = insert.Parameters.Add("$name", SqliteType.Text);
        var weight = insert.Parameters.Add("$weight", SqliteType.Integer);
        foreach (var entry in corpus.Entries())
        {
            id.Value = (long)entry.Key;
            name.Value = entry.Text;
            weight.Value = entry.Weight;
            insert.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    /// <summary>The name of the corpus the index was built from — the short name the report shows.</summary>
    internal string CorpusName { get; }

    /// <summary>Where the content of the index lives.</summary>
    internal string Source { get; }

    /// <summary>The corpus the index was built from.</summary>
    internal Corpus Corpus { get; }

    /// <summary>The index itself.</summary>
    internal ILuiIndex Index { get; }

    /// <summary>The embedded index itself: the write benchmarks write into it.</summary>
    internal ILuiEmbeddedIndex Embedded => Index as ILuiEmbeddedIndex
        ?? throw new InvalidOperationException("The index is not an embedded one — a linked index is written by the application.");

    /// <summary>The journal mode of the database the index lives in — a report without it is a number without a place.</summary>
    internal string JournalMode => _database.Scalar<string>("PRAGMA journal_mode;");

    /// <summary>The entries of the corpus, for the write benchmarks.</summary>
    internal IEnumerable<LuiEntry> Entries => Corpus.Entries();

    /// <summary>
    /// Builds the typo dictionary before the measurement begins, if the case corrects: the build of the dictionary is
    /// the work of the application, not of the query that was measured.
    /// </summary>
    /// <param name="correction">Whether the case corrects an input without a word start.</param>
    internal void WarmUp(bool correction)
    {
        if (!correction)
        {
            return;
        }

        switch (Index)
        {
            case ILuiEmbeddedIndex embedded:
                embedded.WarmUpTypoDictionary();
                break;
            case ILuiLinkedIndex linked:
                linked.WarmUpTypoDictionary();
                break;
            default:
                throw new InvalidOperationException("The index is neither embedded nor linked.");
        }
    }

    /// <summary>The fixtures of the process, released.</summary>
    internal static void DisposeAll()
    {
        lock (Shared)
        {
            foreach (var fixture in Shared.Values)
            {
                fixture.Dispose();
            }

            Shared.Clear();
        }
    }

    public void Dispose()
    {
        _catalog.Dispose();
        _database.Dispose();
    }
}
