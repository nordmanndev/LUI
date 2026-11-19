# LUI

> **Autocomplete suggestions for .NET, straight out of your own SQLite database.** The suggestions of a search field: the best 5–20 entries out of a stock of up to a few million short texts.

[![Build](https://github.com/nordmanndev/LUI/actions/workflows/ci.yml/badge.svg)](https://github.com/nordmanndev/LUI/actions/workflows/ci.yml) [![NuGet](https://img.shields.io/nuget/v/Nordmann.LUI.svg)](https://www.nuget.org/packages/Nordmann.LUI) ![Version](https://img.shields.io/badge/version-0.2.0-8A2BE2) [![Licence](https://img.shields.io/badge/licence-MIT-blue.svg)](./LICENSE) [![.NET](https://img.shields.io/badge/.NET-10.0-512BD4)](https://dotnet.microsoft.com/)

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
dotnet add package Nordmann.LUI            # the library — all a console application needs
dotnet add package Nordmann.LUI.Hosting    # AddLui and the catalog, for ASP.NET Core or a worker
```

The two packages are released together, with one version; the core starts no thread of its own. The version is
`0.2.0`, and the public API is frozen from here on: it grows only additively (see [Status](#status)).

## Quick start

```csharp
using Nordmann.LUI;
using var lui = LuiCatalog.OpenOrCreate("app.db");

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
using var bulkCatalog = LuiCatalog.OpenOrCreate("bulk.db");
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
each of them carries its scenario, its limit, its unit and its kind. The scenario of the latency budgets is three million
single words of the German word list — and the list has 1,908,798 entries, so that scenario is **not reached yet** (#176):
the lane reports those numbers for the entries it has and asserts the sizes (ADR-0041). The numbers in the table are the
lane of 2026-11-19 on a hosted runner (Ubuntu 24.04, 4 cores, .NET 10.0.15) — **not the machine of the budgets**; the method
and the corpus are in [`docs/benchmarks.md`](./docs/benchmarks.md).

| Target | Measured | Limit |
|---|---:|---:|
| query p50 | 0.87 ms | ≤ 2 ms |
| query p95, 1–4 characters | ≤ 1.7 ms | ≤ 5 ms |
| query p95, 5–8 characters | ≤ 4.5 ms | ≤ 20 ms |
| query with a typo correction, p95 | 8.4 ms | ≤ 35 ms |
| allocation per query, p50 / p95 | 20 / 44 KB | ≤ 60 / ≤ 100 KB |
| `Upsert` / `UpdateWeight` / `Remove`, p95 | 0.51–0.78 ms (2026-11-05) | ≤ 1 ms |
| first load of 3 M entries (`Sync`) | 67.0 s for 1.9 M entries | ≤ 3 min (#32) |
| typo dictionary of 685,739 words | 194 MB, 4.0–5.2 s | ≤ 200 MB, ≤ 10 s |
| typo dictionary of two errors, 685,739 words | 388 MB | ≤ 420 MB |
| file of 1 M titles | 243 MB | ≤ 500 MB |

The **limits** live in one file — [`tests/Nordmann.LUI.Testing/Budgets/budgets.json`](./tests/Nordmann.LUI.Testing/Budgets/budgets.json)
(ADR-0056) — and a test reads them out of it and fails when this table drifts from it (issue #38). The **measured
values** are the numbers of the last measurement of each row, with its date and its machine: the query, the allocation, the
build, the file and the dictionary of one error are from the lane of 2026-11-19 (run 9, the entries of the word list — not three million), a write from the CI of
2026-11-05 (the lane does not report it for the entries it has); a time belongs to the machine it was measured on
([ADR-0041](.agents/adr/0041-the-budgets-of-16-run-on-a-schedule.md)). The typo dictionary was measured again on
2026-11-19 — **4.0–5.2 s** for **194 MB** with one error, **10.7–12.2 s** for **388 MB** with two
([`docs/typo-dictionary.md`](./docs/typo-dictionary.md)). The 138 MB and 240 MB that stood here before could not be
reproduced — the same code of `0.1.1`, `0.2.0` and this version gives 194 and 388 MB here (a Linux container and a hosted
runner); why the first measurements were lower is not known.
A measured value does **not** live in the file: it belongs to a run and to its machine, and a file that carried it would
be a second truth beside the promise.

**A write waits for the disk**, so every report names the disk its databases lived on, and a test fails on a report that was
measured on a network drive: on the local disk (ext4) of the run of 2026-11-19 a single write — `Upsert`, `UpdateWeight`,
one transaction each — takes **0.13–0.33 ms** on average (the container of the round, not the machine of the budgets;
issue #178).

**The tables** below are the output of [BenchmarkDotNet](https://benchmarkdotnet.org/) — the code on the corpus of the
measurement, not the scenario of the budgets. They are rendered from the reports of the run in
[`benchmarks/results/`](./benchmarks/results/) by a test, so they cannot drift from them; the machine of the run stands
above them. **Every line names the number of hits its query found** — a line without it can measure nothing and look
healthy (the input `ka` found nothing in the report of the day), and a line that measures no query says so with a dash
(issue #124).

<!-- benchmarks:begin -->
<!-- generated by ReadmeBenchmarksTests from benchmarks/results/ — do not edit by hand -->

```text
BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.5 LTS (Noble Numbat)
Intel Xeon Processor 2.30GHz, 1 CPU, 4 logical and 4 physical cores
.NET SDK 10.0.114
  [Host]     : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v4
  DefaultJob : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v4
  [Run]      : 2026-11-19, the measurements per case: 13–80
  [Machine]  : Intel Xeon Processor 2.30GHz, 1 CPU, 4 logical and 4 physical cores
  [Journal]  : wal
  [Disk]     : ext4, local — /tmp/lui-bench.leE5Mc/tmp
```

#### A query

One query through the whole pipeline: the prefix path for one and two characters, FTS5 from three — Entries = 200000, Candidates = 500, Order = Start/Sim, Similarity = JaroWinkler, Correction = False. `QueryBenchmarks`.

| Method | Corpus | Input | Source | Mean | CI 99.9 % | Allocated | Hits |
|---|---|---|---|---:|---:|---:|---:|
| Search | titles | apfelkuc | Embedded | 80.49 μs | 1.589 μs | 5.2 KB | 0 |
| Search | titles | apfelkuc | Linked | 81.04 μs | 1.598 μs | 5.24 KB | 0 |
| Search | titles | b | Embedded | 138.73 μs | 2.344 μs | 14.88 KB | 500 |
| Search | titles | b | Linked | 346.38 μs | 6.175 μs | 43.22 KB | 500 |
| Search | titles | haus | Embedded | 489.59 μs | 8.890 μs | 51.03 KB | 500 |
| Search | titles | haus | Linked | 564.32 μs | 8.047 μs | 113.76 KB | 500 |
| Search | titles | ka | Embedded | 139.17 μs | 0.329 μs | 14.73 KB | 500 |
| Search | titles | ka | Linked | 366.86 μs | 7.253 μs | 42.64 KB | 500 |
| Search | words | apfelkuc | Embedded | 116.70 μs | 2.260 μs | 6.53 KB | 1 |
| Search | words | apfelkuc | Linked | 116.57 μs | 2.299 μs | 6.57 KB | 1 |
| Search | words | b | Embedded | 143.17 μs | 2.768 μs | 14.63 KB | 500 |
| Search | words | b | Linked | 312.43 μs | 5.459 μs | 39.62 KB | 500 |
| Search | words | haus | Embedded | 597.45 μs | 11.865 μs | 41.49 KB | 500 |
| Search | words | haus | Linked | 537.39 μs | 2.658 μs | 41.59 KB | 500 |
| Search | words | ka | Embedded | 138.76 μs | 1.600 μs | 14.57 KB | 500 |
| Search | words | ka | Linked | 345.86 μs | 6.887 μs | 39.68 KB | 500 |

#### The settings of a query

The budget of the candidates, the order of the ranking, the measure of the similarity and the correction on and off — each of them is measured, not described — Corpus = words, Entries = 200000, Input = haus, Source = Embedded. `SettingsBenchmarks`.

| Method | Candidates | Order | Similarity | Correction | Mean | CI 99.9 % | Allocated | Hits |
|---|---|---|---|---|---:|---:|---:|---:|
| Search | 500 | Start/Sim | JaroWinkler | False | 587.7 μs | 11.69 μs | 41.49 KB | 500 |
| Search | 500 | Start/Sim | JaroWinkler | True | 585.8 μs | 11.57 μs | 41.49 KB | 500 |
| Search | 500 | Start/Sim | Levenshtein | False | 624.6 μs | 12.39 μs | 41.49 KB | 500 |
| Search | 500 | Start/Sim | Levenshtein | True | 621.1 μs | 12.39 μs | 41.49 KB | 500 |
| Search | 500 | WeightOnly | JaroWinkler | False | 489.1 μs | 9.74 μs | 40.75 KB | 500 |
| Search | 500 | WeightOnly | JaroWinkler | True | 518.2 μs | 9.33 μs | 40.75 KB | 500 |
| Search | 500 | WeightOnly | Levenshtein | False | 488.0 μs | 9.72 μs | 40.75 KB | 500 |
| Search | 500 | WeightOnly | Levenshtein | True | 541.4 μs | 12.10 μs | 40.75 KB | 500 |
| Search | 2000 | Start/Sim | JaroWinkler | False | 1,497.6 μs | 33.96 μs | 184.68 KB | 1090 |
| Search | 2000 | Start/Sim | JaroWinkler | True | 1,573.6 μs | 47.77 μs | 184.68 KB | 1090 |
| Search | 2000 | Start/Sim | Levenshtein | False | 1,621.1 μs | 17.95 μs | 184.68 KB | 1090 |
| Search | 2000 | Start/Sim | Levenshtein | True | 1,630.3 μs | 44.72 μs | 184.68 KB | 1090 |
| Search | 2000 | WeightOnly | JaroWinkler | False | 1,212.6 μs | 31.49 μs | 183.82 KB | 1090 |
| Search | 2000 | WeightOnly | JaroWinkler | True | 1,401.5 μs | 42.56 μs | 183.82 KB | 1090 |
| Search | 2000 | WeightOnly | Levenshtein | False | 1,236.7 μs | 31.28 μs | 183.82 KB | 1090 |
| Search | 2000 | WeightOnly | Levenshtein | True | 1,435.7 μs | 41.06 μs | 183.82 KB | 1090 |

#### A query with a typo correction

A single-word index with typo tolerance, the dictionary built; the input has one typo — Corpus = words, Entries = 200000, Candidates = 500, Order = Start/Sim, Similarity = JaroWinkler, Correction = True, Source = Embedded. `CorrectionBenchmarks`.

| Method | Input | Mean | CI 99.9 % | Allocated | Hits |
|---|---|---:|---:|---:|---:|
| Search | bahmhof | 276.8 μs | 5.51 μs | 25.53 KB | 99 |
| Search | fusbal | 561.9 μs | 9.36 μs | 27.98 KB | 118 |
| Search | kartofel | 591.6 μs | 11.83 μs | 23.71 KB | 72 |
| Search | strasee | 1,320.7 μs | 26.40 μs | 42.86 KB | 402 |

#### A single write

One write in a transaction of its own — Entries = 200000. `WriteBenchmarks`.

| Method | Mean | CI 99.9 % | Allocated | Hits |
|---|---:|---:|---:|---:|
| UpdateWeight | 334.6 μs | 12.54 μs | 2.73 KB | — |
| UpsertTheSame | 131.0 μs | 2.60 μs | 3.96 KB | — |
| UpsertAnotherText | 306.9 μs | 23.53 μs | 3.96 KB | — |

#### The search form and the similarity

Per text, no database: the normalisation of ADR-0005 and two similarity measures against `apfelku`; BenchmarkDotNet shortens a long text — `Gedec(...)useln [35]` is „Gedeckter Apfelkuchen mit Streuseln", `Größe(...)nahme [23]` „Größenänderungsmaßnahme". `TextBenchmarks`.

| Method | Text | Mean | CI 99.9 % | Allocated | Hits |
|---|---|---:|---:|---:|---:|
| SearchForm | Apfelkuchen | 46.64 ns | 0.980 ns | 144 B | — |
| JaroWinkler | Apfelkuchen | 46.61 ns | 0.959 ns | - | — |
| NgramDice | Apfelkuchen | 97.39 ns | 1.580 ns | - | — |
| SearchForm | Gedec(...)useln [35] | 110.36 ns | 2.260 ns | 240 B | — |
| JaroWinkler | Gedec(...)useln [35] | 69.94 ns | 1.397 ns | - | — |
| NgramDice | Gedec(...)useln [35] | 240.97 ns | 4.597 ns | - | — |
| SearchForm | Größe(...)nahme [23] | 109.29 ns | 2.154 ns | 320 B | — |
| JaroWinkler | Größe(...)nahme [23] | 71.28 ns | 1.460 ns | - | — |
| NgramDice | Größe(...)nahme [23] | 163.47 ns | 3.304 ns | - | — |

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

The typo tolerance is a decision of the application: the dictionary of the words of an index costs 194 MB for 685,739
words, so it is **off by default** and switched on with `TypoTolerance()`.

| | **Typo tolerance off** (the default) | **Typo tolerance on** |
|---|---|---|
| **`SingleWord`** | the text as one unit: `apfelkuch` finds *Apfelkuchen* | + one error in the word: `apfelkuh` finds *Apfelkuchen* |
| **`MultiWord`** | the words ANDed: `apfelkuch` finds *Apfel Kuchen*, `kuchen apfel` finds it too | + one error in one of the words: `apfle kuchen` finds *Apfel Kuchen* |

A correction happens only when **no candidate** matches the input at the beginning of a word — 34 % of the typos would
find hits in the middle of other words. The dictionary belongs to the index: a query in a tag index never corrects
towards the words of a name index.

**The dictionary is built on request.** `WarmUpTypoDictionary()` reads the content table and builds it — at three million
entries that is seconds and 194 MB, which the application should not pay at the start. A query never builds it: it
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
| **Your own file** | possible — a catalogue of embedded indexes alone | not possible — the index is in the file of your table |

The two are independent of the kind of the index: every combination of them exists. **One catalogue is one file**: an
`Embedded` and a `Linked` index live side by side in it (mixing them is defined), and what separates them is **who owns
the content table** — the library (`lui_<index>`) or your application. A `Linked` index keeps the
normalised text in the search index and the weight and the priority in the order of the candidates — your row itself
is never copied.

## A payload

A hit can carry a payload of your own: a category, a colour, an id of yours. It is any type of the JSON context of
your application, written by the **serialiser of the catalog** — source-generated, and so safe for trimming and AOT.

```csharp
using Nordmann.LUI;
using var withChips = LuiCatalog.OpenOrCreate("chips.db", new LuiCatalogOptions
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
builder.Services.AddLui(_ => LuiCatalog.OpenOrCreate("app.db"));
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
`OpenReadOnly` writes nothing — no WAL, no `lui_meta` — and every write is refused with a `LuiReadOnlyException`.

```csharp
// the same file, opened for reading only: it reads and searches, and it writes nothing
using var reader = LuiCatalog.OpenReadOnly("app.db");
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
| `LuiCatalog` | the entry point: `Open`, `Create`, `CreateLinked`, `Index`, `LinkedIndex`, `DropIndex` |
| `LuiEntry` | what is written: a key, a text, a weight, a priority, a payload |
| `LuiKey` | the key of the application: a number or a text — `1` and `"1"` are different keys |
| `LuiQuery` | a query that is configured step by step: `Take`, `Candidates`, `Rank`, `PriorityMode`, `Similarity`, `TypoTolerance`, `TrackTimings` |
| `LuiQueryOptions` | the same settings as a record, for the configuration of an application |
| `LuiResult` | the answer of a query: the hits, the candidates it considered, the query it corrected |
| `LuiHit` | one hit: the key, the text, the weight, the priority, the similarity — and `GetPayload<T>()` |
| `LuiCatalogOptions` | the settings of the catalog: the pragmas of the connections (busy timeout, synchronous, cache size), the payload serialiser, the clock |

One catalogue is one file: the file of your application becomes a catalogue with `OpenOrCreate`, and its tables lie beside
the tables of LUI. LUI sets write-ahead logging only when `OpenOrCreate` creates the file itself; a file that is already there, with tables
or empty, keeps its mode.

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
| **[The testing strategy](./docs/testing-strategy.md)** | why the tests are built the way they are — the layers, the techniques and their reason |
| **[What applies to a library](./docs/architecture/alignment.md)** and **[the domain](./docs/domain/lui.md)** | the engineering hygiene of a library — and the terms the library speaks |

## Status

LUI is at **`0.3.0`**. The public API was frozen from `0.1.0`
([ADR-0026](.agents/adr/0026-the-public-api-from-0-1-0-on-grows-only-additively.md)); the freeze was lifted for this
version, and its breaks are named in the migration note of the [CHANGELOG](./CHANGELOG.md)
([ADR-0069](.agents/adr/0069-the-freeze-of-0-1-0-is-lifted-for-0-3-0.md)). It holds again from `0.3.0` on. The design is
specified, and the implementation follows it; every decision of the repository is written down and dated.

Two packages, one version: `Nordmann.LUI` (the library) and `Nordmann.LUI.Hosting` (the dependency injection). The core
takes `Microsoft.Data.Sqlite` and `symspell` and nothing else; the target framework is `net10.0`, the licence is MIT.
From `0.3.0` on the public API grows only additively: a new type, a new method, a new overload, a new `init` property.
A breaking change — a removed or renamed member, a changed signature — needs a new minor version, a migration note in
the [CHANGELOG](./CHANGELOG.md) and an ADR. Before `0.1.0` such a change was free; `0.3.0` was the one exception, by decision
of the owner; now it is a promise again.

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
