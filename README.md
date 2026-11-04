# LUI

> **LUI** — autocomplete suggestions for .NET, straight out of your own SQLite
> database. The suggestions of a search field: the best 5–20 entries out of a
> stock of up to a few million short texts.

LUI is a **library**, not a server. It works in your application's SQLite
database and uses **SQLite FTS5** as its search engine — no index to maintain
in parallel, no second datastore, no service to operate.

```text
A user types into a search field  →  LUI answers with the best entries
```

---

## What this is

| | |
|---|---|
| **For** | short texts (1–10 words): tags, words, names, recipe and book titles |
| **Built for** | much reading, little writing (typically 98 % queries) |
| **Aligned to** | the **beginning** of a word — what someone types is usually the start of what they are looking for |
| **Tolerant of** | typos, umlauts, and word order |
| **Runs on** | .NET 10, SQLite ≥ 3.45 (via `Microsoft.Data.Sqlite`) |

## What this is **not**

- **Not a full-text search engine.** No BM25, no stemming, no phrase search, no
  facets — for that, use Lucene, SeekStorm or FTS5 directly.
- **Not a key–value store.**
- **Not a server** with its own HTTP interface.

---

## Status

LUI is **feature-complete for 0.1.0 and not yet published**. The design is
specified in [`docs/spec/LUI-Spezifikation.md`](./docs/spec/LUI-Spezifikation.md),
the contract of this repository; the implementation followed the plan in
[`docs/plan/2026-11-02-lui-repo-plan.md`](./docs/plan/2026-11-02-lui-repo-plan.md),
one pull request per step, and every decision is an ADR in [`docs/adr/`](./docs/adr/).

| | |
|---|---|
| Packages | `Nordmann.LUI` (the library) and `Nordmann.LUI.Hosting` (the dependency injection), one version — [ADR-0018](./docs/adr/0018-two-packages-core-and-hosting.md) |
| Target framework | `net10.0` |
| Dependencies of the core | `Microsoft.Data.Sqlite`, `symspell`, `F23.StringSimilarity` — nothing else ([ADR-0004](./docs/adr/0004-dependencies.md)) |
| Licence | MIT |
| Current version | `0.1.0` (not yet published) |

**The public API is frozen from `0.1.0` on, and grows only additively** ([ADR-0026](./docs/adr/0026-the-public-api-from-0-1-0-on-grows-only-additively.md)):
a removed or renamed type or member, a changed signature or a changed meaning of a documented behaviour needs a new minor
version, a migration note in the [CHANGELOG](./CHANGELOG.md) and an ADR. A new member of an interface comes as a default
member, so an application's own implementation of it keeps compiling; the exceptions are named in the ADR (a new enum value
may meet an exhaustive `switch`; the `Equals` and `ToString` of the records are not part of the promise).

The public API follows the specification: a `LuiCatalog`, an index per
searchable stock, a fluent query builder, and the two index kinds
`SingleWord` and `MultiWord`. The code of this README is **executed by the
package smoke test** — every C# block, in order, against the packed `.nupkg` —
so the README can never drift from the code.

## Install

```text
dotnet add package Nordmann.LUI            # the library — all a console application needs
dotnet add package Nordmann.LUI.Hosting    # AddLui and the hosted services, for ASP.NET Core or a worker
```

The two packages are released together, with one version. The core starts no
thread of its own; whatever runs in the background belongs to the host.

The package is not on nuget.org yet. The publish pipeline is in place and
waits for its tag.

## Quick start

```csharp
using Nordmann.LUI;

using var lui = LuiCatalog.Open("Data Source=app.db");

// an index of titles: words in any order, spaces optional, one typo forgiven
var recipes = lui.Index("recipes").MultiWord().TypoTolerance().Ensure();

recipes.Upsert(1, "Gedeckter Apfelkuchen", weight: 9000);
recipes.Upsert(2, "Apfelstrudel", weight: 5000);
recipes.Upsert(3, "Bratapfelkuchen", weight: 800);

foreach (var hit in recipes.Search("apfelkuch").Hits)
{
    Console.WriteLine($"{hit.Text} ({hit.Weight})");   // Gedeckter Apfelkuchen (9000), then Bratapfelkuchen (800)
}

Console.WriteLine(recipes.Search("apfel gedeckt").Hits[0].Text);   // Gedeckter Apfelkuchen
Console.WriteLine(recipes.Search("gedekter").CorrectedQuery);         // gedeckter
```

Load a stock in **one** transaction, never entry by entry — `UpsertMany` is
about four times faster than a loop of `Upsert`s:

```csharp
recipes.UpsertMany(Enumerable.Range(1, 10_000).Select(i =>
    new LuiEntry<long, LuiNoPayload>(100 + i, $"Rezept Nummer {i}") { Weight = i }));
```

A query with all its knobs is a fluent builder; `Search(text, options)` takes
the same settings from configuration:

```csharp
var result = recipes.Query("rezept")
    .Take(5)
    .Rank(LuiRankingOrder.WordStartThenWeight)
    .TrackTimings()
    .Execute();

Console.WriteLine($"{result.Hits.Count} of {result.CandidatesConsidered} candidates in {result.Timings!.Total.TotalMilliseconds:F1} ms");
```

Keep an index in step with your own data with `Sync`: what the source has is
added or written, what it lacks is removed — in batches, merged at the end, and
a sync that was cancelled can simply run again. `Check()` says whether an index
is sound and what repairs it:

```csharp
var report = recipes.Sync(new[] { "Gedeckter Apfelkuchen", "Apfelstrudel", "Bratapfelkuchen" }
    .Select((title, i) => new LuiEntry<long, LuiNoPayload>(i + 1, title) { Weight = 1000 * (3 - i) }));

Console.WriteLine($"{report.Changed} changed, {report.Removed} removed");   // 3 changed, 10000 removed
Console.WriteLine(recipes.Check().IsHealthy);                                // True
```

---

## The two index kinds

The kind of an index is fixed when it is created and decides how it is
searched. The caller does not have to know how.

| | `SingleWord` | `MultiWord` |
|---|---|---|
| For | tags, single words, compounds, hashtags | names, recipe titles, book titles |
| Examples | `Apfelkuchen`, `#Bahnhof`, `vegan` | `Anna Müller`, `Gedeckter Apfelkuchen` |
| Input | as a whole | split into words and ANDed; also matched compactly |
| Word order | — | irrelevant (`kuchen apfel` = `apfel kuchen`) |
| Spaces | — | irrelevant (`apfelkuch` finds „Apfel Kuchen") |

## The two content sources

| | `Embedded` (the default) | `Linked` |
|---|---|---|
| Content table | `lui_<index>` — the library's own | a table of your application |
| Who writes | the application, via `Upsert` / `UpsertMany` / `Remove` | the application, with ordinary SQL |
| Index ↔ content | consistent by triggers | the application: `Refresh(id)` after its writes (in its transaction, if it likes), `Sync()` for everything — or triggers it asks for |
| Your schema changed | — | **never**: no column, no index, no trigger unless you call for one |
| Text stored twice | yes | no |

```csharp
using Microsoft.Data.Sqlite;

// a table of the application, as it is
using (var app = new SqliteConnection("Data Source=app.db"))
{
    app.Open();
    using var create = app.CreateCommand();
    create.CommandText = """
        CREATE TABLE IF NOT EXISTS tag (id INTEGER PRIMARY KEY, name TEXT NOT NULL, usage_count INTEGER NOT NULL);
        INSERT OR REPLACE INTO tag VALUES (1, 'vegan', 120), (2, 'vegetarisch', 80), (3, 'glutenfrei', 40);
        """;
    create.ExecuteNonQuery();
}

// linked: LUI reads the table and never changes it
var tags = lui.Index("tags").SingleWord().LinkTo(table: "tag", key: "id", text: "name", weight: "usage_count").Ensure();
tags.Sync();                                             // the whole table; then tags.Refresh(id) after a write

Console.WriteLine(tags.Search("veg").Hits[0].Text);     // vegan
```

Triggers are opt-in (`tags.TriggerScript()` for your migration, or `tags.CreateTriggers()`): they only note the key of
a changed row, so any SQLite can still write your table, and `tags.ApplyPending()` brings the noted rows into the
index — call it regularly; `tags.Pending()` is a good value for a health check.

---

## The query

`index.Search(text)` takes the defaults; `index.Query(text)` is the fluent builder, and `Search(text, options)` takes
the same settings as a `LuiQueryOptions` — from configuration, for example. Every value is checked where it is set.

| Builder | `LuiQueryOptions` | Default | What it does |
|---|---|---|---|
| `.Take(n)` | `MaxResults` | 20 | how many hits come back (1 … 1,000) |
| `.Candidates(n)` | `Candidates` | 500 | how many candidates are ranked — chosen by priority and weight (1 … 100,000) |
| `.Rank(order)` | `Rank` | `WordStartThenSimilarity` | the order of the ranking, see below |
| `.PriorityMode(mode)` | `PriorityMode` | `First` | whether the priority comes before everything (`First`) or only makes an entry a certain candidate (`CandidatesOnly`) |
| `.DistinctText()` | `DistinctText` | off | one hit per normalised text — the one of the highest priority, then weight |
| `.Similarity(measure)` | `Similarity` | of the index | `LuiSimilarity.JaroWinkler` (the default of `SingleWord`), `NgramDice` (of `MultiWord`), `DamerauLevenshtein`, `Levenshtein` — or your own `ILuiSimilarity` |
| `.TypoTolerance(bool)` | `TypoTolerance` | of the index | whether an input that no word begins with is corrected |
| `.TrackTimings()` | `TrackTimings` | off | `result.Timings`: candidate read, correction, ranking, payload, total |

A `LuiResult` holds the `Hits` (key, text, weight, priority, payload, whether a word begins with the input, the
similarity), `CandidatesConsidered`, `Truncated` (the candidate budget was spent), `CorrectedQuery` and
`CorrectionSkipped`.

## Ranking and priority

What a user feels, in the order it is applied ([spec §9](./docs/spec/LUI-Spezifikation.md)):

| Stage | Rule | Example for `apfelkuch` |
|---|---|---|
| 0. Priority | an entry of a higher `LuiPriority` (`System` → `High` → `Elevated` → `Normal`) comes first | the system tag before the user's |
| 1. Word start | a word of the entry begins with the input — compactly, so spaces do not matter | **Apfelkuch**en, Gedeckter **Apfelkuch**en, **Apfel Kuch**en ✅ · Brat**apfelkuch**en ❌ |
| 2. Similarity | measured from the word the hit begins at, not from the start of the text | „Gedeckter Apfelkuchen" scores 0.99, not 0.38 |
| 3. Weight | the more popular first | 9000 before 800 |

`LuiRankingOrder` reorders stages 1–3: `WordStartThenSimilarity` (the default), `WordStartThenWeight`,
`SimilarityOnly`, `WeightOnly`. A one- or two-character input is ranked by weight alone — every candidate begins with
it. A tie on every stage is broken by the rowid, so the result is deterministic.

**Why the priority comes first:** the candidates are read in the order of priority, then weight, and only 500 of them
are ranked. A rare system tag would otherwise lose against thousands of popular entries before the ranking ever saw
it — with a priority it is a candidate for every input it matches:

```csharp
var labels = lui.Index("labels").SingleWord().Ensure();
labels.UpsertMany([
    new LuiEntry<long, LuiNoPayload>(1, "Vegetarisch") { Weight = 900 },
    new LuiEntry<long, LuiNoPayload>(2, "Vegan") { Weight = 3, Priority = LuiPriority.System },
]);

Console.WriteLine(labels.Search("veg").Hits[0].Text);   // Vegan
```

## Typo tolerance

Off by default — the dictionary costs memory — and switched on per index (`.TypoTolerance()` on the builder) or per
query. When it triggers, and what it does ([spec §10](./docs/spec/LUI-Spezifikation.md)):

- **When:** no candidate has a word that begins with the input — not only when nothing is found: a third of all typos
  match somewhere in the middle of another word.
- **How:** every word of three or more letters is looked up in a SymSpell dictionary of the word *beginnings* of the
  index (one edit), the three best corrections are searched as well, and the ranking runs with the corrected input.
  `CorrectedQuery` says what was searched (`gedeckter` for `gedekter`).
- **The dictionary** is built on the first correction (138 MB and about 4 s for 685,829 words), or ahead of time with
  `WarmUpTypoDictionary()`. Writes through LUI add their words at once.
- **No query waits for a build.** While another thread builds the dictionary, a query that needs a correction answers
  without it and sets `CorrectionSkipped`.
- **The dictionary only grows.** It is built once and learns the words of every write; nothing rebuilds it behind your
  back. A word that was removed lingers — harmless, because the index is the filter: a correction towards a word that
  is gone finds no candidate and falls away. `RebuildTypoDictionary()` forgets removed words, in a quiet phase if you
  care for the memory.
- **Every index has a dictionary of its own**, built from its own texts: a search among tags is never corrected
  towards names, nor the other way round.

```csharp
recipes.RebuildTypoDictionary();
Console.WriteLine(recipes.TypoDictionaryState);   // Current
```

## In an ASP.NET Core or worker application

`Nordmann.LUI.Hosting` brings the dependency injection: `AddLui` registers the catalog, and every index as a keyed service under its
name — the read interface for a suggest endpoint, the write interface for an
import. The indexes are ensured when the host starts (a definition that does not
match stops the start, not the first request); a linked index can have its
outbox applied on a timer:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder();
builder.Services
    .AddLui("Data Source=app.db")
    .AddIndex("recipes", index => index.MultiWord().TypoTolerance())
    .AddLinkedIndex("tags", index => index.SingleWord().LinkTo(table: "tag", key: "id", text: "name", weight: "usage_count"),
              applyPendingEvery: TimeSpan.FromSeconds(5))
    .WarmUpTypoDictionariesOnStart();

using var host = builder.Build();
host.Start();

// in an endpoint: [FromKeyedServices("recipes")] ILuiIndex<long, LuiNoPayload> recipes
var suggest = host.Services.GetRequiredKeyedService<ILuiIndex<long, LuiNoPayload>>("recipes");
Console.WriteLine(suggest.Search("apfelkuch").Hits[0].Text);   // Gedeckter Apfelkuchen
```

`AddLui` opens the database when the host starts — also with `EnsureOnStart(false)`, because the hosted services
resolve the catalog first, so that the container disposes them before it. A SQLite that LUI cannot run on (older than 3.45,
or without the FTS5 trigram tokenizer) therefore stops the start with a `LuiEnvironmentException`. It, the
`LuiDefinitionMismatchException` of an index that exists with another definition and the `LuiLinkedTableException` share
the base `LuiException` — an `InvalidOperationException`, so a `catch` of that one still catches them.

The host does not rebuild typo dictionaries on its own (they only grow); `WarmUpTypoDictionariesOnStart()` builds
them in the background after the start, and no query waits for that build: one that would need a correction meanwhile
answers without it and says `CorrectionSkipped`.

Without a host — in a console application — the same is a few calls of your
own: `Ensure()` where you open an index, `WarmUpTypoDictionary()` at the start,
`RebuildTypoDictionary()` in a quiet phase if removed words should go, and `ApplyPending()` after your writes
or in a `PeriodicTimer` loop.

---

## Performance

Measured, with the machine named — a number without a machine is a rumour. The full record, the method and every
earlier run are in [`docs/benchmarks.md`](./docs/benchmarks.md).

**The targets of [spec §16](./docs/spec/LUI-Spezifikation.md)**, each asserted by
[`PerformanceBudgets`](./tests/Nordmann.LUI.Integration.Tests/PerformanceBudgets.cs) in its scenario — three million
single words of the German word list (685,829 words, then compounds), one million titles for the file
([ADR-0020](./docs/adr/0020-the-performance-budgets-measured.md)). Intel Xeon 2.1 GHz, 4 cores, 15 GB, Ubuntu 24.04,
.NET 10.0.14:

| Target | Measured | Limit |
|---|---:|---:|
| query p50 | 1.34–1.71 ms | ≤ 2 ms |
| query p95, 1–4 characters | ≤ 3.1 ms | ≤ 5 ms |
| query p95, 5–8 characters | 13.4–17.5 ms | ≤ 20 ms |
| query with a typo correction, p95 | 27.9–32.5 ms | ≤ 35 ms |
| allocation per query, p50 / p95 | 83 / 298 KB | ≤ 250 / ≤ 350 KB (too much — #30) |
| `Upsert` / `UpdateWeight` / `Remove`, p95 | 0.51–0.78 ms | ≤ 1 ms |
| first load of 3 M entries (`Sync`) | 172.5–205.9 s | ≤ 4 min (#32) |
| typo dictionary of 685,829 words | 138 MB, 4.1–4.8 s | ≤ 200 MB, ≤ 10 s |
| file of 1 M titles | 457 MB | ≤ 500 MB |

Five of these were stated anew by the owner after they were measured — the query p95, the correction, the
allocation, the file and the first load; for three of them (long inputs, the correction, the file) a loosening,
written down as such in ADR-0020.

### BenchmarkDotNet

[`benchmarks/Nordmann.LUI.Benchmarks`](./benchmarks/Nordmann.LUI.Benchmarks/) measures the code, not the scenario:
an index of 200,000 entries of the same generated corpus (seed 42), built by `Sync`, in a database in the page cache.
The tables below are **generated** from BenchmarkDotNet's own reports in
[`benchmarks/results/`](./benchmarks/results/) — a test (`Readme_Benchmarks`) fails when they differ, so a number here
always has a run behind it. Run them again with `tools/benchmarks.sh`.

<!-- benchmarks:begin -->
<!-- generated by Readme_Benchmarks from benchmarks/results/ — do not edit by hand -->

```text
BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.5 LTS (Noble Numbat)
Intel Xeon Processor 2.10GHz, 1 CPU, 4 logical and 4 physical cores
.NET SDK 10.0.114
  [Host]     : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v4
  DefaultJob : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v4
```

#### A query

One query through the whole pipeline: the prefix path for one and two characters, FTS5 from three — Entries = 200000. `QueryBenchmarks`.

| Method | Kind | Input | Mean | Error | Allocated |
|---|---|---|---:|---:|---:|
| Search | SingleWord | apfelkuc | 113.19 μs | 3.739 μs | 26.76 KB |
| Search | SingleWord | b | 470.02 μs | 15.352 μs | 62.98 KB |
| Search | SingleWord | haus | 687.29 μs | 25.171 μs | 142.73 KB |
| Search | SingleWord | ka | 23.79 μs | 0.622 μs | 24.53 KB |
| Search | MultiWord | apfelkuc | 191.86 μs | 4.885 μs | 25.05 KB |
| Search | MultiWord | b | 595.32 μs | 21.216 μs | 86.31 KB |
| Search | MultiWord | haus | 1,070.80 μs | 38.348 μs | 185.05 KB |
| Search | MultiWord | ka | 634.05 μs | 21.595 μs | 86.85 KB |

#### A query with a typo correction

A single-word index with typo tolerance, the dictionary built; the input has one typo — Entries = 200000. `CorrectionBenchmarks`.

| Method | Input | Mean | Error | Allocated |
|---|---|---:|---:|---:|
| Search | bahmhof | 274.8 μs | 8.99 μs | 99.78 KB |
| Search | fusbal | 476.6 μs | 13.83 μs | 80.15 KB |
| Search | kartofel | 113.5 μs | 3.29 μs | 26.84 KB |
| Search | strasee | 123.7 μs | 3.58 μs | 26.79 KB |

#### A single write

One write in a transaction of its own — Entries = 200000. `WriteBenchmarks`.

| Method | Mean | Error | Allocated |
|---|---:|---:|---:|
| UpdateWeight | 343.48 μs | 11.133 μs | 436 B |
| UpsertTheSame | 92.93 μs | 1.835 μs | 812 B |
| UpsertAnotherText | 228.23 μs | 18.042 μs | 812 B |

#### The search form and the similarity

Per text, no database: the normalisation of §6 and two similarity measures against `apfelku`; BenchmarkDotNet shortens a long text — `Gedec(...)useln [35]` is „Gedeckter Apfelkuchen mit Streuseln", `Größe(...)nahme [23]` „Größenänderungsmaßnahme". `TextBenchmarks`.

| Method | Text | Mean | Error | Allocated |
|---|---|---:|---:|---:|
| SearchForm | Apfelkuchen | 57.31 ns | 1.250 ns | 144 B |
| JaroWinkler | Apfelkuchen | 106.18 ns | 2.163 ns | 208 B |
| NgramDice | Apfelkuchen | 106.25 ns | 2.332 ns | - |
| SearchForm | Gedec(...)useln [35] | 129.86 ns | 3.891 ns | 240 B |
| JaroWinkler | Gedec(...)useln [35] | 155.27 ns | 4.885 ns | 232 B |
| NgramDice | Gedec(...)useln [35] | 286.61 ns | 6.816 ns | - |
| SearchForm | Größe(...)nahme [23] | 135.20 ns | 4.214 ns | 320 B |
| JaroWinkler | Größe(...)nahme [23] | 185.13 ns | 3.672 ns | 200 B |
| NgramDice | Größe(...)nahme [23] | 201.63 ns | 4.061 ns | - |

<!-- benchmarks:end -->

**Reading.** These are 200,000 entries on one thread, in the page cache — the code, not the scenario of §16. A query
takes 24 µs (two letters, the prefix path of a single-word index) to 1.1 ms (`haus` in titles: many candidates to
rank); its allocation follows the candidates it reads (#30). A correction costs one more search per correction tried.
A single write is a commit of its own; load a stock with `UpsertMany` or `Sync` instead.

---

## Sample application

```bash
dotnet run --project samples/Lui.Sample      # http://localhost:5000
```

[`samples/Lui.Sample/`](./samples/Lui.Sample/) is an ASP.NET Core application that serves a page with three autocompletes, built
with [flexdatalist](https://github.com/sergiodlopes/flexdatalist) and the whole of the API behind them
([ADR-0027](./docs/adr/0027-the-sample-flexdatalist-modular-client-one-fetch-boundary.md)):

| Autocomplete | Index | What it shows |
|---|---|---|
| Recipes | embedded, multi-word, **JSON payload** (category, minutes, emoji) | the payload as groups and properties, a priority badge, typo correction as "Did you mean …?", transliteration |
| Tags | **linked** to the application's own table, columns as the payload, priority `System` | a tag picker with chips and a colour from the payload; "add a tag" writes a row and tells the index (triggers, outbox) |
| Names | embedded, multi-word, no payload | the typo tolerance — with a dictionary of its own per index |

![The recipes autocomplete: "kartofel" is corrected, the page asks "Did you mean “kartoffel”?", the hits are grouped by the category of their payload](./docs/images/sample-recipes.png)

![The tag picker over the application's own table: chips, a colour from the payload column, the priority System first](./docs/images/sample-tags.png)

Every setting of a query is a control (`Take`, `Candidates`, `Rank`, `PriorityMode`, `DistinctText`, `Similarity`,
`TypoTolerance`, `TrackTimings`) and a parameter of the request. Beside the input the page shows what flexdatalist drops — the
whole `LuiResult`: the corrected query, the candidates considered, the timings — the hits as ranked, the requests (a request that is
abandoned while typing is aborted, and the server stops its work), and the indexes with their maintenance (`Sync`, `Check`,
`Rebuild`, `Optimize`, the typo dictionary, the outbox, the triggers).

The client is plain ES modules in folders by concern, with one fetch boundary and no build; `npm test` in the sample's folder runs
the tests of its modules. LUI delivers no found positions ([ADR-0028](./docs/adr/0028-lui-has-no-highlight-a-correction-is-shown-as-a-question.md)):
when the typo tolerance corrected the input, the page says so — "Did you mean …?" from `CorrectedQuery` — and marks nothing.

---

## Repository layout

```text
src/Nordmann.LUI/            ← the library (package Nordmann.LUI)
src/Nordmann.LUI.Hosting/    ← the dependency injection (package Nordmann.LUI.Hosting, same version)
tests/                       ← unit, architecture and integration tests
                              (and the map of them: docs/testing.md)
benchmarks/                  ← BenchmarkDotNet, and its reports (benchmarks/results/)
samples/Lui.Sample/          ← ASP.NET Core API + a page of ES modules with flexdatalist (ADR-0027)
tools/                       ← the smoke test, the mutation check, the benchmark run, the word list
docs/adr/                    ← the decisions, and why
docs/spec/                   ← the specification this repo implements
docs/plan/                   ← the delivery plan
```

## Working on LUI

Read [`AGENTS.md`](./AGENTS.md) first — it is the contract for this
repository, and it inherits
[`nordmanndev/standards`](https://github.com/nordmanndev/standards).

- **One issue, one pull request**, each with its tests, its measurement and — for a decision that changes the public
  API, the schema or the dependencies — its ADR.
- **The specification is the contract.** Code that contradicts it is a bug, unless an ADR says the specification is
  wrong; then both change in the same pull request.
- **Before a pull request:** `dotnet test`, `dotnet format --verify-no-changes`, `tools/package-smoke-test.sh` and
  `tools/mutation-check.sh`. The map of the tests ([`docs/testing.md`](./docs/testing.md)) is a guard: a test file it
  does not list fails the build.

## Licence

MIT — see [`LICENSE`](./LICENSE).
