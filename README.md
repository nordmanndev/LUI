# LUI

> **Autocomplete suggestions for .NET, straight out of your own SQLite database.** The suggestions of a search field: the best 5–20 entries out of a stock of up to a few million short texts.

[![Build](https://github.com/nordmanndev/LUI/actions/workflows/ci.yml/badge.svg)](https://github.com/nordmanndev/LUI/actions/workflows/ci.yml) [![NuGet](https://img.shields.io/nuget/vpre/Nordmann.LUI.svg)](https://www.nuget.org/packages/Nordmann.LUI) ![Version](https://img.shields.io/badge/version-0.1.0--alpha-8A2BE2) [![Licence](https://img.shields.io/badge/licence-MIT-blue.svg)](./LICENSE) [![.NET](https://img.shields.io/badge/.NET-10.0-512BD4)](https://dotnet.microsoft.com/)

## What LUI is

LUI is a **library**, not a server: it puts a fast, typo-tolerant search in front of the data that is already in your
SQLite database. It uses **SQLite FTS5** as its search engine — no second datastore, no service to operate.

**What this is not.** Not a full-text search engine — no BM25, no stemming, no phrase search, no facets; for that,
use Lucene, SeekStorm or FTS5 directly. Not a key–value store. Not a server with its own HTTP interface.

## The problem

98 % of the input of an application is a read: a search field, a dropdown, an autocomplete. The expensive part of such a feature is not the ranking, but the **second store** for the same data — the index beside the database.

## Highlights

- **No second datastore.** The search index lives in your SQLite file, next to your rows.
- **Your rows stay yours.** A `Linked` index keeps no copy of your table and never changes your schema.
- **One typo forgiven.** The correction is per index, off by default, and a query never waits for it.
- **Measured, not promised.** Every performance number has a run behind it, with the machine named.
- **No types in the index.** The type of a payload stands where you use it: `hit.GetPayload<TagChip>()`.

## Contents

- [What LUI is](#what-lui-is) · [The problem](#the-problem) · [Highlights](#highlights) · [Install](#install) · [Quick start](#quick-start)
- [Loading many entries](#loading-many-entries) · [Performance](#performance) · [The kinds of an index](#the-kinds-of-an-index) · [Embedded or linked](#embedded-or-linked)
- [A payload](#a-payload) · [In an ASP.NET Core application](#in-an-aspnet-core-application) · [Only read](#only-read) · [The API](#the-api)
- [The sample application](#the-sample-application) · [The documentation](#the-documentation) · [Status](#status) · [Working on LUI](#working-on-lui) · [Licence](#licence)

## Install

```text
dotnet add package Nordmann.LUI --prerelease            # the library — all a console application needs
dotnet add package Nordmann.LUI.Hosting --prerelease    # AddLui and the catalog, for ASP.NET Core or a worker
```

The two packages are released together, with one version; the core starts no thread of its own. The version is
`0.1.0-alpha`, a prerelease: the API may still change until `0.1.0` (see [Status](#status)).

## Quick start

```csharp
using Nordmann.LUI;
using var lui = LuiCatalog.Open("Data Source=app.db");

// an index of titles: the words in any order, the spaces optional, one typo forgiven
var recipes = lui.Create("recipes", index => index.MultiWord().TypoTolerance());

recipes.Upsert(1, "Gedeckter Apfelkuchen", weight: 9000);
recipes.Upsert(3, "Bratapfelkuchen", weight: 800);

// the application asks for the dictionary of the typos — a query never does
recipes.WarmUpTypoDictionary();

foreach (var hit in recipes.Search("apfelkuch").Hits)
{
    Console.WriteLine($"{hit.Text} ({hit.Weight})");
}

Console.WriteLine(recipes.Search("apfel gedeckt").Hits[0].Text);
Console.WriteLine(recipes.Search("gedekter").CorrectedQuery);
```

```text
Gedeckter Apfelkuchen (9000)
Bratapfelkuchen (800)
Gedeckter Apfelkuchen
gedeckter
```

`Create` writes — it belongs in the migration of your application; in the runtime path the index is **opened**: `lui.Index("recipes")` reads the definition out of the file and writes nothing.

## Loading many entries

An index that holds more than a few hundred entries is **loaded**, not written one by one. `Sync` makes the index equal
to a set of entries: it reads them in batches, writes them in one transaction per batch, and removes what is gone.

```csharp
// many records at once: Sync makes the index equal to the entries, in batches
using var bulkCatalog = LuiCatalog.Open("Data Source=bulk.db");
var words = bulkCatalog.Create("words", index => index.SingleWord());

var report = words.Sync(
    Enumerable.Range(0, 50_000).Select(i => new LuiEntry(i, $"Wort {i}")),
    new LuiSyncOptions { BatchSize = 10_000 });

Console.WriteLine($"{report.Read} read, {report.Added} added, {report.Batches} batches");
```

```text
50000 read, 50000 added, 5 batches
```

- **`Sync`** is the bulk path: one transaction per batch of `BatchSize` entries, a report at the end, and a
  `IProgress<LuiSyncReport>` for the progress of the load.
- **`UpsertMany`** is the same without the removal: it writes the entries it is given and leaves the rest alone.
- **`Sync`** also takes a stream: `await index.Sync(entries.AsAsyncEnumerable(), …)`.
- **The cost** of a load of a million entries is measured — see [Performance](#performance).

A `Linked` index is loaded differently: its content is the table of the application, so `Sync()` reads the table and
indexes it. The rows that are written afterwards are taken in with `Refresh(id)`, `RefreshMany(ids)` or the triggers the
application asks for.

## Performance

Two measurements, two machines — and both name the machine they stand on.

**The budgets** below are the promises of the repository: they live in one file
([`tests/Nordmann.LUI.Testing/Budgets/budgets.json`](./tests/Nordmann.LUI.Testing/Budgets/budgets.json), ADR-0056), and
each of them carries its scenario, its limit, its unit and its kind. They are measured in their scenario — three
million single words of the German word list and one million titles — on the CI machine: Intel Xeon 2.10 GHz, 4 cores,
15 GB, Ubuntu 24.04, .NET 10.0.14. Every target is asserted by a test in its scenario; the method and the corpus are
in [`docs/benchmarks.md`](./docs/benchmarks.md).

| Target | Measured | Limit |
|---|---:|---:|
| query p50 | 1.34–1.71 ms | ≤ 2 ms |
| query p95, 1–4 characters | ≤ 3.1 ms | ≤ 5 ms |
| query p95, 5–8 characters | 13.4–17.5 ms | ≤ 20 ms |
| query with a typo correction, p95 | 27.9–32.5 ms | ≤ 35 ms |
| allocation per query, p50 / p95 | 46 / 51 KB | ≤ 60 / ≤ 100 KB |
| `Upsert` / `UpdateWeight` / `Remove`, p95 | 0.51–0.78 ms | ≤ 1 ms |
| first load of 3 M entries (`Sync`) | 172.5–205.9 s | ≤ 4 min (#32) |
| typo dictionary of 685,739 words | 138 MB, 4.1–4.8 s | ≤ 200 MB, ≤ 10 s |
| typo dictionary of two errors, 685,739 words | 240 MB | ≤ 240 MB |
| file of 1 M titles | 457 MB | ≤ 500 MB |
The **limits** live in one file — [`tests/Nordmann.LUI.Testing/Budgets/budgets.json`](./tests/Nordmann.LUI.Testing/Budgets/budgets.json)
(ADR-0056) — and a test reads them out of it and fails when this table drifts from it (issue #38). The **measured
values** are the numbers of the last measurement of each row: the latencies, the build and the size of the file from
the CI machine (2026-11-05, before the allocation was lowered — so they are an upper bound); the allocation in bytes,
and therefore independent of the machine (2026-11-06, the current code). A measured value does **not** live in the
file: it belongs to a run and to its machine, and a file that carried it would be a second truth beside the promise.

The **limits** live in one file — [`tests/Nordmann.LUI.Testing/Budgets/budgets.json`](./tests/Nordmann.LUI.Testing/Budgets/budgets.json)
(ADR-0056) — and a test reads them out of it and fails when this table drifts from it (issue #38). The **measured
values** are the numbers of the last measurement of each row: the latencies, the build and the size of the file from
the CI machine (2026-11-05, before the allocation was lowered — so they are an upper bound); the allocation in bytes,
and therefore independent of the machine (2026-11-06, the current code); the typo dictionary in bytes and in seconds
(2026-11-06, the machine of the record — a time belongs to the machine it was measured on,
[ADR-0041](.agents/adr/0041-the-budgets-of-16-run-on-a-schedule.md)). The typo dictionary was measured again on
2026-11-12 — **4.3 s** for the same **138 MB** ([`docs/typo-dictionary.md`](./docs/typo-dictionary.md)): a time belongs
to the machine and the date it was measured on (ADR-0041), the memory is the claim that does not depend on the machine.
A measured value does **not** live in the file: it belongs to a run and to its machine, and a file that carried it would
be a second truth beside the promise.

**The tables** below are the output of [BenchmarkDotNet](https://benchmarkdotnet.org/) — the code on the corpus of the
measurement, not the scenario of the budgets. They are rendered from the reports of the run in
[`benchmarks/results/`](./benchmarks/results/) by a test, so they cannot drift from them; the machine of the run stands
above them. **Every line names the number of hits its query found** — a line without it can measure nothing and look
healthy (the input `ka` found nothing in the report of the day), and a line that measures no query says so with a dash
(issue #124).

<!-- benchmarks:begin -->
<!-- generated by ReadmeBenchmarksTests from benchmarks/results/ — do not edit by hand -->

```text
BenchmarkDotNet v0.15.8, Linux Debian GNU/Linux 12 (bookworm)
Intel Xeon Platinum 2.50GHz, 1 CPU, 2 logical and 2 physical cores
.NET SDK 10.0.403
  [Host]     : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v4
  DefaultJob : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v4
  [Run]      : 2026-11-13, the measurements per case: 23–95
  [Machine]  : Intel Xeon Platinum 2.50GHz, 1 CPU, 2 logical and 2 physical cores
  [Journal]  : wal
```

#### A query

One query through the whole pipeline: the prefix path for one and two characters, FTS5 from three — Entries = 200000, Candidates = 500, Order = Start/Sim, Similarity = JaroWinkler, Correction = False. `QueryBenchmarks`.

| Method | Corpus | Input | Source | Mean | Error | Allocated | Hits |
|---|---|---|---|---:|---:|---:|---:|
| Search | titles | apfelkuc | Embedded | 131.5 μs | 2.61 μs | 5.78 KB | 0 |
| Search | titles | apfelkuc | Linked | 133.5 μs | 2.65 μs | 5.83 KB | 0 |
| Search | titles | b | Embedded | 495.0 μs | 9.86 μs | 43.62 KB | 500 |
| Search | titles | b | Linked | 540.3 μs | 10.79 μs | 43.76 KB | 500 |
| Search | titles | haus | Embedded | 736.8 μs | 14.45 μs | 51.57 KB | 500 |
| Search | titles | haus | Linked | 842.5 μs | 16.80 μs | 114.3 KB | 500 |
| Search | titles | ka | Embedded | 525.9 μs | 10.45 μs | 43.04 KB | 500 |
| Search | titles | ka | Linked | 565.3 μs | 11.22 μs | 43.18 KB | 500 |
| Search | words | apfelkuc | Embedded | 191.2 μs | 3.78 μs | 7.07 KB | 1 |
| Search | words | apfelkuc | Linked | 184.7 μs | 3.69 μs | 7.11 KB | 1 |
| Search | words | b | Embedded | 476.4 μs | 9.48 μs | 40.02 KB | 500 |
| Search | words | b | Linked | 490.5 μs | 9.52 μs | 40.16 KB | 500 |
| Search | words | haus | Embedded | 809.7 μs | 15.95 μs | 42.03 KB | 500 |
| Search | words | haus | Linked | 808.9 μs | 15.86 μs | 42.13 KB | 500 |
| Search | words | ka | Embedded | 517.5 μs | 10.29 μs | 40.09 KB | 500 |
| Search | words | ka | Linked | 519.4 μs | 10.36 μs | 40.22 KB | 500 |

#### The settings of a query

The budget of the candidates, the order of the ranking, the measure of the similarity and the correction on and off — each of them is measured, not described — Corpus = words, Entries = 200000, Input = haus, Source = Embedded. `SettingsBenchmarks`.

| Method | Candidates | Order | Similarity | Correction | Mean | Error | Allocated | Hits |
|---|---|---|---|---|---:|---:|---:|---:|
| Search | 500 | Start/Sim | JaroWinkler | False | 811.6 μs | 16.10 μs | 42.03 KB | 500 |
| Search | 500 | Start/Sim | JaroWinkler | True | 827.9 μs | 16.45 μs | 42.03 KB | 500 |
| Search | 500 | Start/Sim | Levenshtein | False | 847.7 μs | 16.89 μs | 42.03 KB | 500 |
| Search | 500 | Start/Sim | Levenshtein | True | 840.5 μs | 16.72 μs | 42.03 KB | 500 |
| Search | 500 | WeightOnly | JaroWinkler | False | 773.0 μs | 14.11 μs | 41.29 KB | 500 |
| Search | 500 | WeightOnly | JaroWinkler | True | 757.3 μs | 15.07 μs | 41.29 KB | 500 |
| Search | 500 | WeightOnly | Levenshtein | False | 767.1 μs | 15.31 μs | 41.29 KB | 500 |
| Search | 500 | WeightOnly | Levenshtein | True | 765.1 μs | 15.30 μs | 41.29 KB | 500 |
| Search | 2000 | Start/Sim | JaroWinkler | False | 1,712.7 μs | 33.40 μs | 185.22 KB | 1090 |
| Search | 2000 | Start/Sim | JaroWinkler | True | 1,686.9 μs | 33.40 μs | 185.22 KB | 1090 |
| Search | 2000 | Start/Sim | Levenshtein | False | 1,807.9 μs | 35.87 μs | 185.22 KB | 1090 |
| Search | 2000 | Start/Sim | Levenshtein | True | 1,810.0 μs | 36.14 μs | 185.22 KB | 1090 |
| Search | 2000 | WeightOnly | JaroWinkler | False | 1,587.5 μs | 31.42 μs | 184.36 KB | 1090 |
| Search | 2000 | WeightOnly | JaroWinkler | True | 1,594.7 μs | 31.51 μs | 184.36 KB | 1090 |
| Search | 2000 | WeightOnly | Levenshtein | False | 1,581.9 μs | 31.31 μs | 184.36 KB | 1090 |
| Search | 2000 | WeightOnly | Levenshtein | True | 1,602.5 μs | 31.76 μs | 184.36 KB | 1090 |

#### A query with a typo correction

A single-word index with typo tolerance, the dictionary built; the input has one typo — Corpus = words, Entries = 200000, Candidates = 500, Order = Start/Sim, Similarity = JaroWinkler, Correction = True, Source = Embedded. `CorrectionBenchmarks`.

| Method | Input | Mean | Error | Allocated | Hits |
|---|---|---:|---:|---:|---:|
| Search | bahmhof | 57.91 μs | 1.156 μs | 5.52 KB | 0 |
| Search | fusbal | 125.30 μs | 2.447 μs | 5.52 KB | 0 |
| Search | kartofel | 281.90 μs | 5.553 μs | 5.53 KB | 0 |
| Search | strasee | 329.98 μs | 6.509 μs | 5.52 KB | 0 |

#### A single write

One write in a transaction of its own — Entries = 200000. `WriteBenchmarks`.

| Method | Mean | Error | Allocated | Hits |
|---|---:|---:|---:|---:|
| UpdateWeight | 2,037.0 μs | 40.30 μs | 436 B | — |
| UpsertTheSame | 699.1 μs | 10.20 μs | 820 B | — |
| UpsertAnotherText | 2,148.9 μs | 42.38 μs | 820 B | — |

#### The search form and the similarity

Per text, no database: the normalisation of §6 and two similarity measures against `apfelku`; BenchmarkDotNet shortens a long text — `Gedec(...)useln [35]` is „Gedeckter Apfelkuchen mit Streuseln", `Größe(...)nahme [23]` „Größenänderungsmaßnahme". `TextBenchmarks`.

| Method | Text | Mean | Error | Allocated | Hits |
|---|---|---:|---:|---:|---:|
| SearchForm | Apfelkuchen | 50.28 ns | 1.072 ns | 144 B | — |
| JaroWinkler | Apfelkuchen | 56.23 ns | 1.155 ns | - | — |
| NgramDice | Apfelkuchen | 140.19 ns | 2.634 ns | - | — |
| SearchForm | Gedec(...)useln [35] | 113.71 ns | 2.311 ns | 240 B | — |
| JaroWinkler | Gedec(...)useln [35] | 92.13 ns | 1.582 ns | - | — |
| NgramDice | Gedec(...)useln [35] | 330.39 ns | 6.620 ns | - | — |
| SearchForm | Größe(...)nahme [23] | 113.51 ns | 2.237 ns | 320 B | — |
| JaroWinkler | Größe(...)nahme [23] | 93.69 ns | 1.479 ns | - | — |
| NgramDice | Größe(...)nahme [23] | 227.96 ns | 4.578 ns | - | — |

<!-- benchmarks:end -->

## The kinds of an index

The kind is decided **once**, when the index is created, and it decides how a query searches. The caller of a query does
not have to know it.

| | **`SingleWord`** | **`MultiWord`** |
|---|---|---|
| **For** | tags, single words, compounds, hashtags | names, recipe and book titles |
| **Examples** | `Apfelkuchen`, `#Bahnhof`, `vegan` | `Anna Müller`, `Gedeckter Apfelkuchen` |
| **The input is** | one unit | split into words, **ANDed** |
| **The order of the words** | — | does not matter: `kuchen apfel` finds what `apfel kuchen` finds |
| **The spaces** | — | do not matter: `apfelkuch` finds “Apfel Kuchen” |
| **The default similarity** | Jaro-Winkler | N-gram Dice |

### The four combinations

The typo tolerance is a decision of the application: the dictionary of the words of an index costs 138 MB for 685,739
words, so it is **off by default** and switched on with `TypoTolerance()`.

| | **Typo tolerance off** (the default) | **Typo tolerance on** |
|---|---|---|
| **`SingleWord`** | the text as one unit: `apfelkuch` finds *Apfelkuchen* | + one error in the word: `apfelkuh` finds *Apfelkuchen* |
| **`MultiWord`** | the words ANDed: `apfelkuch` finds *Apfel Kuchen*, `kuchen apfel` finds it too | + one error in one of the words: `apfle kuchen` finds *Apfel Kuchen* |

A correction happens only when **no candidate** matches the input at the beginning of a word — 34 % of the typos would
find hits in the middle of other words. The dictionary belongs to the index: a query in a tag index never corrects
towards the words of a name index.

**The dictionary is built on request.** `WarmUpTypoDictionary()` reads the content table and builds it — at three million
entries that is seconds and 138 MB, which the application should not pay at the start. A query never builds it: it
answers with what the input found, sets `CorrectionSkipped` and waits for the application to ask for the build.

## Embedded or linked

The content of an index comes either from the library's own table or from a table of the application. Three questions
decide it.

1. **Do the rows already live in a table of your application?**
   **Yes** → take **Linked**: the library indexes your table and keeps no copy of the text.
   **No** → take **Embedded**: the library keeps the text in its own table `lui_<index>`.
2. **Who writes those rows?**
   **You, with SQL or with your ORM** → with **Embedded** you write the text twice: once into your table, once into
   the index. With **Linked** you write it once and tell the index afterwards.
   **Nobody yet — the texts come from a file, an API or a list** → **Embedded**; the index is the only place the text is.
3. **Do you need a weight of your own — clicks, a popularity, a stock?**
   **Yes** → with **Embedded** the weight is a property of the entry. With **Linked** it is a column of your table, and
   the index reads it.

| | **`Embedded`** (the default) | **`Linked`** |
|---|---|---|
| **The text lives in** | the table of the library, `lui_<index>` | your own table |
| **You write it with** | `Upsert`, `UpsertMany`, `Remove` | ordinary SQL — then `Refresh(id)`, `Sync()` or the triggers you ask for |
| **A copy of the text** | yes | **no** |
| **Your schema** | untouched | untouched — no column, no index, no trigger unless you ask for one |
| **A hit reads** | the copy in the index | your row, live |
| **Your own file** | possible | not possible — the index is in the file of your table |

The two are independent of the kind of the index: every combination of them exists. A `Linked` index keeps the
normalised text in the search index and the weight and the priority in the order of the candidates — your row itself
is never copied.

## A payload

A hit can carry a payload of your own: a category, a colour, an id of yours. It is any type of the JSON context of
your application, written by the **serialiser of the catalog** — source-generated, and so safe for trimming and AOT.

```csharp
using Nordmann.LUI;
using var withChips = LuiCatalog.Open("Data Source=chips.db", new LuiCatalogOptions
{
    PayloadSerializer = LuiPayloadSerializer.Json(TagJson.Default),   // one serialiser for every index of the catalog
});

var chips = withChips.Create("chips", index => index.SingleWord());
chips.Upsert(new LuiEntry(1, "vegan") { Weight = 120, Payload = new TagChip("#3a3", "System") });

var chip = chips.Search("veg").Hits[0].GetPayload<TagChip>();   // the type stands where the payload is used
var typed = chips.As<TagChip>().Search("veg").Hits[0].GetPayload();  // the same, through a view — the compiler checks the type
Console.WriteLine(chip!.Colour);                                     // #3a3
Console.WriteLine(typed!.Colour);                                    // #3a3
```

```csharp
// The types of the application: a payload is any type of its JSON context
using System.Text.Json.Serialization;

public sealed record TagChip(string Colour, string Group);

[JsonSerializable(typeof(TagChip))]
public sealed partial class TagJson : JsonSerializerContext;
```

The index knows no types: the file stores the payload as bytes, and the type stands where the payload is used — a
payload that cannot be read is a `LuiPayloadException`. An application that wants the type checked by the compiler asks
for a **view** of the index: `chips.As<TagChip>()` — and a fluent query ends in the same typed answer with
`.Execute().As<TagChip>()`. The view is a view — it opens nothing, writes nothing, and holds no index of its own.

## In an ASP.NET Core application

`Nordmann.LUI.Hosting` brings the dependency injection: `AddLui` puts the **catalog** into the container — and
nothing else. An index is not registered and not injected; the application asks the catalog for it.

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder();
builder.Services.AddLui("Data Source=app.db");
builder.Services.AddLuiWarmup();     // optional: the dictionaries at the start, the outbox every 30 s, told to the logger

using var host = builder.Build();
host.Start();

// in an endpoint: the catalog is injected — the index is asked for
var catalog = host.Services.GetRequiredService<LuiCatalog>();
var suggest = catalog.Index("recipes");
Console.WriteLine(suggest.Search("apfelkuch").Hits[0].Text);   // Gedeckter Apfelkuchen
```

The hosting package carries the dependency injection — and, if you ask for it, the **warm-up**:
`AddLuiWarmup()` registers a service of the host: the typo dictionaries of your indexes are built while the host starts
(so the first query already corrects a typo — the host is ready when they are), and the outbox of a linked index — the
rows the triggers noted — is applied every 30 seconds. A dictionary costs memory: name the indexes that correct typos
with `AddLuiWarmup(options => options.TypoDictionaryIndexes.Add("recipes"))`. What the service did, and a round that
failed, it tells the logger of the host. **Off by default**: without the call, the host starts nothing of LUI.

The library starts no thread of its own — the host runs the service, stops it and disposes it. And the library still
carries no timer of its own: the rebuild of a dictionary stays with your application, because only you know when a
phase is quiet.

## Only read

Sometimes an application may only read: a replica, a file that is read-only. A catalog opened with
`Mode=ReadOnly` writes nothing — no WAL, no `lui_meta` — and every write is refused with a `LuiReadOnlyException`.

```csharp
// the same file, opened for reading only: it reads and searches, and it writes nothing
using var reader = LuiCatalog.Open("Data Source=app.db;Mode=ReadOnly");
var opened = reader.Index("recipes");

foreach (var hit in opened.Search("apfelkuch").Hits)
{
    Console.WriteLine($"{hit.Key}: {hit.Text}");   // 1: Gedeckter Apfelkuchen
}
```

`Index` reads the definition out of the file and writes nothing — no table, no definition, no write lock. The file
decides what may happen.

## The API

| Type | What it is |
|---|---|
| `LuiCatalog` | the entry point: `Open`, `Create`, `Index`, `LinkedIndex`, `DropIndex` |
| `LuiEntry` | what is written: a key, a text, a weight, a priority, a payload |
| `LuiKey` | the key of the application: a number or a text — `1` and `"1"` are different keys |
| `LuiQuery` | a query that is configured step by step: `Take`, `Candidates`, `Rank`, `PriorityMode`, `Similarity`, `TypoTolerance`, `TrackTimings` |
| `LuiQueryOptions` | the same settings as a record, for the configuration of an application |
| `LuiResult` | the answer of a query: the hits, the candidates it considered, the query it corrected |
| `LuiHit` | one hit: the key, the text, the weight, the priority, the similarity — and `GetPayload<T>()` |
| `LuiCatalogOptions` | the settings of the catalog: the cache, the memory map, the busy timeout, the WAL, the payload serialiser, the clock |

**Every public type of the package is listed in [`docs/api.md`](./docs/api.md)** — the catalogue, the builders, the
index, the entry, the query, the payload, the similarity, the enumerations, the exceptions and the hosting extension,
with a sentence for each.

## The sample application

```bash
dotnet run --project samples/Lui.Sample      # http://localhost:5000
```

[`samples/Lui.Sample/`](./samples/Lui.Sample/) is an ASP.NET Core application with six autocompletes: recipes with
a JSON payload, tags **linked** to your own table, names, the German word list and two bulk indexes. The bulk indexes
are **created on demand** — the page fills them when you ask for it, and shows the progress while it does.

![The sample application: the recipes with the correction of a typo, the tags over the table of the application, and the fill of a bulk index on demand — with its progress and the query on the index it just filled](./docs/images/sample.gif)

![The recipes autocomplete: "kartofel" is corrected, the page asks "Did you mean “kartoffel”?", the hits are grouped by the category of their payload](./docs/images/sample-recipes.png)

![The tag picker over the application's own table: chips, a colour from the payload column, the priority System first](./docs/images/sample-tags.png)

## The documentation

| | |
|---|---|
| **[The map of the documentation](./docs/README.md)** | what lies where: the entrance, the way, the API, the numbers — and who reads what |
| **[The tutorial](./docs/tutorial/README.md)** | the way: from an empty database to a working autocomplete, in five steps |
| **[The API](./docs/api.md)** | every public type of the package, with a sentence for each |
| **[The benchmarks](./docs/benchmarks.md)** | every measured number, with the machine, the corpus and the method |
| **[The normalisation](./docs/normalization.md)** | the rule that turns a text into a searchable form — and what was measured about it |
| **[The test map](./docs/testing.md)** | which test proves which rule |
| **[What applies to a library](./docs/architecture/alignment.md)** and **[the domain](./docs/domain/lui.md)** | the engineering hygiene of a library — and the terms the library speaks |

## Status

LUI is **feature-complete for 0.1.0**, and its first release is the prerelease **`0.1.0-alpha`**
([ADR-0053](.agents/adr/0053-the-first-release-is-a-prerelease.md)). The design is specified, and the implementation
follows it; every decision of the repository is written down and dated.

Two packages, one version: `Nordmann.LUI` (the library) and `Nordmann.LUI.Hosting` (the dependency injection). The core
takes `Microsoft.Data.Sqlite` and `symspell` and nothing else; the target framework is `net10.0`, the licence is MIT,
and the public API grows only additively from `0.1.0` on. The alpha is to try out: until `0.1.0` an API may still
change, each change with an ADR and an entry in the [CHANGELOG](./CHANGELOG.md).

## Working on LUI

Read [`AGENTS.md`](./AGENTS.md) first — it states the rules of this repository: one issue, one pull request, an ADR
with every decision that changes the public API, and a test with every rule.

- **One issue, one pull request**, each with its tests, its measurement and — for a decision that changes the public
  API, the schema or the dependencies — its record.
- **Before a pull request:** `dotnet test`, `dotnet format --verify-no-changes`, `tools/package-smoke-test.sh`,
  `tools/mutation-check.sh` — the test map ([`docs/testing.md`](./docs/testing.md)) is a guard: an unlisted test file fails the build.
- **The code of this README runs.** Every C# block above is executed by the package smoke test, against the packed
  `.nupkg` — so the README cannot drift from the code.

## Licence

MIT — see [`LICENSE`](./LICENSE).
