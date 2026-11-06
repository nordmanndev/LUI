# 4 — Many entries

Three entries are a demo. A hundred thousand entries are an application, and
they behave differently: the load is a bulk load, the limits start to matter,
and the cost is worth knowing before you fill the file.

This step is about the two questions that come with a real stock: **how do I
load it**, and **what does it cost**.

## One transaction, not ten thousand

`Upsert` writes one entry in a transaction of its own. That is the right thing
for a single write — it is committed at once, and a caller may see it right
away. For a stock it is the wrong thing: ten thousand entries are ten thousand
commits.

`UpsertMany` writes many entries in **one** transaction. All of them are
written, or — on an error or a cancellation — none.

```csharp
using Nordmann.LUI;

using var catalog = LuiCatalog.Open("Data Source=app.db");
var recipes = catalog.Create("recipes", index => index.MultiWord());

// Ten thousand entries in one transaction — not ten thousand transactions.
var written = recipes.UpsertMany(
    Enumerable.Range(1, 10_000).Select(i => new LuiEntry(100 + i, $"Rezept Nummer {i}") { Weight = i }));

Console.WriteLine($"{written} entries written");
```

```text
10000 entries written
```

The difference is measured: 100,000 single-word entries took **16.9 s** as
100,000 single `Upsert`s and **3.8 s** as one `UpsertMany` — 4.4× faster. The
numbers are from [`docs/benchmarks.md`](../../docs/benchmarks.md) (the run
`BatchPerformance`); the machine is named there.

> **Why so?**
> A commit is a round trip to the disk, and the disk is the slow part. The
> work of an `Upsert` is small; the cost of saying "that is settled" is not.
> One transaction says it once.

## The sync

`Sync` brings an index in step with a **source** — a table of another
database, a file, a service. It adds what is missing, changes what differs,
leaves alone what is the same, and at the end removes the keys the source does
not have.

It works in batches, each in a transaction of its own, so that other writers
get a chance between them. The batch size and the progress are yours to set.

```csharp
using Nordmann.LUI;

using var catalog = LuiCatalog.Open("Data Source=app.db");
var recipes = catalog.Create("recipes", index => index.MultiWord());

var report = recipes.Sync(
    Enumerable.Range(1, 10_000).Select(i => new LuiEntry(100 + i, $"Rezept Nummer {i}") { Weight = i }),
    new LuiSyncOptions
    {
        BatchSize = 100_000,
        Progress = new WritingProgress(),
    });

Console.WriteLine(
    $"{report.Added} added, {report.Changed} changed, {report.Unchanged} unchanged, {report.Removed} removed");

/// <summary>Writes the report of a sync as it arrives.</summary>
public sealed class WritingProgress : IProgress<LuiSyncReport>
{
    public void Report(LuiSyncReport report) => Console.WriteLine($"{report.Read} read");
}
```

```text
10000 read
10000 read
0 added, 0 changed, 10000 unchanged, 0 removed
```

The sync above found the ten thousand entries of the step before unchanged —
that is what `Unchanged` means: the entries that were in the index as they are
in the source, and were not written.

> **Why so?**
> `Progress<T>` hands the report to the thread pool — the write happens a
> moment later, and a program that ends right away may never print it. A
> progress that writes at once is an `IProgress<T>` of your own, like the one
> above: the interface is the contract, `Progress<T>` is only the usual
> implementation of it.

A sync that is cancelled or fails keeps the batches it committed. Run it
again: it skips what is already there and finishes the rest. The removal at
the end happens only when the whole source was read — so a cancelled sync
never removes what it did not see.

## The check

An index can be out of step — a write that went past the library, a file that
was copied, a crash in the middle of a batch. `Check` says whether it is
sound and what is wrong; `Rebuild` repairs what is below the content table.

```csharp
using Nordmann.LUI;

using var catalog = LuiCatalog.Open("Data Source=app.db");
var recipes = catalog.Create("recipes", index => index.MultiWord());

var report = recipes.Check(
    Enumerable.Range(1, 10_000).Select(i => new LuiEntry(100 + i, $"Rezept Nummer {i}") { Weight = i }));

Console.WriteLine($"healthy: {report.IsHealthy}");

recipes.Rebuild();

Console.WriteLine($"healthy: {recipes.Check().IsHealthy}");
```

```text
healthy: True
healthy: True
```

`Check()` without a source checks the index against itself: the stored
definition is the one of this instance, the search index passes the integrity
check of FTS5, and the triggers are in place.

## The limits

Every limit is checked where it is set, and the exception names the property.

| Setting | Default | Limit | What it is |
|---|---:|---:|---|
| `LuiQueryOptions.MaxResults` | 20 | 1,000 | how many hits come back — they are read in one statement |
| `LuiQueryOptions.Candidates` | 500 | 100,000 | how many candidates are ranked; what is not among them is not ranked |
| `LuiSyncOptions.BatchSize` | 10,000 | 1,000,000 | the entries per batch, each in a transaction of its own |
| `LuiEntry.Weight` | 0 | 536,870,911 | the popularity |
| `LuiPriority` | `Normal` | — | `System`, `High`, `Elevated`, `Normal`; a smaller number ranks first |

The candidate budget is the one to watch: it is the price of a query. A query
that does not fill it is cheap; a query that does reads and ranks 500
candidates. What is not among the candidates is not ranked — and the
candidates are chosen by priority and weight, so the budget decides what the
ranking ever sees.

## What it costs

Every number below is measured. The budget table was measured on Intel Xeon
2.1 GHz, 4 cores, 15 GB, Ubuntu 24.04, .NET 10.0.14; the machine of every
other run is named in the file itself. The full record, the method and every
earlier run are in
[`docs/benchmarks.md`](../../docs/benchmarks.md).

**A query**, three million single words of the German word list:

| | Measured | Limit |
|---|---:|---:|
| query p50 | 1.34–1.71 ms | ≤ 2 ms |
| query p95, 1–4 characters | ≤ 3.1 ms | ≤ 5 ms |
| query p95, 5–8 characters | 13.4–17.5 ms | ≤ 20 ms |
| query with a typo correction, p95 | 27.9–32.5 ms | ≤ 35 ms |
| allocation per query, p50 / p95 | 83 / 298 KB | ≤ 250 / ≤ 350 KB |

**A write**, one at a time, in a transaction of its own: p95 **0.51–0.78 ms**
(limit ≤ 1 ms).

**The build**, three million entries in one first load with `Sync`:
**172.5–174.2 s** on one machine and **194.9–205.9 s** on the next (limit
≤ 240 s). A first load into a file no one else writes is about 30 % faster
with a batch of 100,000 than with 10,000, and faster still with 1,000,000:
three million entries in 172.5 s against 190 s.

**The file**: one million titles of 36 characters on average take **457 MB**
(limit ≤ 500 MB). An embedded index holds the text and the derived forms of
its search index in the file; a linked index holds the text only in your own
table — that is the argument for step 2 when the stock is large.

**The typo dictionary**: 685,829 words take **138 MB** and **4.1–4.8 s** to
build (limits: ≤ 200 MB, ≤ 10 s). It is built when the first input needs a
correction, or by `WarmUpTypoDictionary()` at the start of the application —
so that the first typo does not pay for it.

**A sync of a million entries**, single word, batches of 10,000: **77.8 s**
into an empty index, **15.2 s** when nothing changes. The same load with
`UpsertMany` in one transaction took **52.5 s**, but the query afterwards was
slower: p50 1.68 ms against 1.22 ms, because `Sync` writes the search index
sorted and merges it at the end, and `UpsertMany` builds it row by row.

> **Why so?**
> The numbers are not a warning, they are a map. The p95 of a long input
> (13.4–17.5 ms) is the cost of FTS5's own trigram expression over a
> synthetic corpus of three million entries; a search field waits 100–300 ms
> between two keystrokes, so it is not felt there. The allocation per query
> follows the candidate budget — 500 candidates is a decision, not an
> accident. See
> [ADR-0020](../../docs/adr/0020-the-performance-budgets-measured.md) and
> [ADR-0032](../../docs/adr/0032-the-allocation-of-a-query.md).

## What you have now

- A bulk load that does not commit ten thousand times.
- A sync that can be interrupted and run again.
- A check that tells you whether the index is sound.
- The limits, and the cost of every one of them.

Next: the same library in a running application — step 5,
[In the application](./05-in-der-anwendung.md).
