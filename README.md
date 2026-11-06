# LUI

> **Autocomplete suggestions for .NET, straight out of your own SQLite database.** The suggestions of a search field: the best 5–20 entries out of a stock of up to a few million short texts.

[![Build](https://github.com/nordmanndev/LUI/actions/workflows/ci.yml/badge.svg)](https://github.com/nordmanndev/LUI/actions/workflows/ci.yml) [![NuGet](https://img.shields.io/nuget/v/Nordmann.LUI.svg)](https://www.nuget.org/packages/Nordmann.LUI) ![Version](https://img.shields.io/badge/version-0.1.0%20unpublished-8A2BE2) [![Licence](https://img.shields.io/badge/licence-MIT-blue.svg)](./LICENSE) [![.NET](https://img.shields.io/badge/.NET-10.0-512BD4)](https://dotnet.microsoft.com/)

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

- [What LUI is](#what-lui-is) · [The problem](#the-problem) · [Highlights](#highlights) · [Install](#install) · [Quick start](#quick-start) · [Performance](#performance) · [The two index kinds and the two content sources](#the-two-index-kinds-and-the-two-content-sources)
- [A payload](#a-payload) · [In an ASP.NET Core application](#in-an-aspnet-core-application) · [Only read](#only-read) · [The most important types](#the-most-important-types) · [The sample application](#the-sample-application) · [The documentation](#the-documentation) · [Status](#status) · [Working on LUI](#working-on-lui) · [Licence](#licence)

## Install

```text
dotnet add package Nordmann.LUI            # the library — all a console application needs
dotnet add package Nordmann.LUI.Hosting    # AddLui and the catalog, for ASP.NET Core or a worker
```

The two packages are released together, with one version; the core starts no thread of its own. They are not on nuget.org yet — the publish pipeline waits for its tag.

## Quick start

```csharp
using Nordmann.LUI;
using var lui = LuiCatalog.Open("Data Source=app.db");

// an index of titles: the words in any order, the spaces optional, one typo forgiven
var recipes = lui.Create("recipes", index => index.MultiWord().TypoTolerance());

recipes.Upsert(1, "Gedeckter Apfelkuchen", weight: 9000);
recipes.Upsert(3, "Bratapfelkuchen", weight: 800);

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

## Performance

Measured on the CI machine — Intel Xeon 2.10 GHz, 4 cores, 15 GB, Ubuntu 24.04, .NET 10.0.14 — with three million
single words of the German word list and one million titles, each target asserted by
[`PerformanceBudgets`](./tests/Nordmann.LUI.Integration.Tests/PerformanceBudgets.cs) in its scenario
([ADR-0020](./docs/adr/0020-the-performance-budgets-measured.md)); the full record is in [`docs/benchmarks.md`](./docs/benchmarks.md).

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

## The two index kinds and the two content sources

| | | What it is | What it is for | What the application does | What LUI keeps |
|---|---|---|---|---|---|
| **`SingleWord`** | the kind of an index | tags, single words, compounds, hashtags | searches the whole text as one unit | the text as it is |
| **`MultiWord`** | the kind of an index | names, recipe and book titles | searches the words of the text, ANDed — the order and the spaces do not matter | the text, word by word |
| **`Embedded`** | the source of the content (the default) | the library's own table `lui_<index>` | writes with `Upsert`, `UpsertMany`, `Remove` | a copy of the text, beside the search index |
| **`Linked`** | the source of the content | a table of your application | writes with ordinary SQL, then `Refresh(id)`, `Sync()` or the triggers it asks for | **no copy of the row**: the normalised text in the search index, the weight and the priority in the order of the candidates |

The kinds and the sources are independent — every combination of them exists. A `Linked` index never changes your
schema: no column, no index, no trigger, unless you ask for one ([ADR-0015](./docs/adr/0015-the-linked-index-is-loosely-coupled.md)).

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
Console.WriteLine(chip!.Colour);                                 // #3a3
```

```csharp
// The types of the application: a payload is any type of its JSON context
using System.Text.Json.Serialization;

public sealed record TagChip(string Colour, string Group);

[JsonSerializable(typeof(TagChip))]
public sealed partial class TagJson : JsonSerializerContext;
```

The index knows no types ([ADR-0035](./docs/adr/0035-the-index-knows-no-types.md)): the file stores the payload as
bytes, and the type stands where the payload is used — a payload that cannot be read is a `LuiPayloadException`.

## In an ASP.NET Core application

`Nordmann.LUI.Hosting` brings the dependency injection: `AddLui` puts the **catalog** into the container — and
nothing else. An index is not registered and not injected; the application asks the catalog for it.

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder();
builder.Services.AddLui("Data Source=app.db");

using var host = builder.Build();
host.Start();

// in an endpoint: the catalog is injected — the index is asked for
var catalog = host.Services.GetRequiredService<LuiCatalog>();
var suggest = catalog.Index("recipes");
Console.WriteLine(suggest.Search("apfelkuch").Hits[0].Text);   // Gedeckter Apfelkuchen
```

The hosting package carries the dependency injection and nothing else
([ADR-0034](./docs/adr/0034-the-hosting-package-carries-only-the-dependency-injection.md)) — no background service, no timer.

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

`Index` reads the definition out of the file and writes nothing — no table, no definition, no write lock
([ADR-0033](./docs/adr/0033-the-read-path-opens-an-index-without-writing.md)). The file decides what may happen.

## The most important types

| Type | What it is |
|---|---|
| `LuiCatalog` | the entry point: `Open`, `Create`, `Index`, `LinkedIndex`, `DropIndex` |
| `LuiEntry` | what is written: a key, a text, a weight, a priority, a payload |
| `LuiKey` | the key of the application: a number or a text — `1` and `"1"` are different keys |
| `LuiQueryOptions` | the settings of a query, for the configuration: `MaxResults`, `Candidates`, `Rank`, `PriorityMode`, `DistinctText`, `Similarity`, `TypoTolerance`, `TrackTimings` |
| `LuiCatalogOptions` | the settings of the catalog: the cache, the memory map, the busy timeout, the WAL, the payload serialiser, the clock |

A query answers with a `LuiResult`: the hits, the candidates it considered and the query it corrected.

## The sample application

```bash
dotnet run --project samples/Lui.Sample      # http://localhost:5000
```

[`samples/Lui.Sample/`](./samples/Lui.Sample/) is an ASP.NET Core application with six autocompletes: recipes with
a JSON payload, tags **linked** to your own table, names, the German word list and two bulk indexes.

![The recipes autocomplete: "kartofel" is corrected, the page asks "Did you mean “kartoffel”?", the hits are grouped by the category of their payload](./docs/images/sample-recipes.png)

![The tag picker over the application's own table: chips, a colour from the payload column, the priority System first](./docs/images/sample-tags.png)

## The documentation

| | |
|---|---|
| **[The map of the documentation](./docs/README.md)** | what lies where: the contract, the way, the history — and who reads what |
| **[The tutorial](./docs/tutorial/README.md)** | the way: from an empty database to a working autocomplete, in five steps |
| **[The specification](./docs/spec/LUI-Spezifikation.md)** | the contract: the kinds of index, the sources of the content, the query path, the ranking, the typo tolerance, the API |
| **[The benchmarks](./docs/benchmarks.md)** | every measured number, with the machine, the corpus and the method |
| **[The normalisation](./docs/normalization.md)** | the rule that turns a text into a searchable form — and what was measured about it |
| **[The decisions](./docs/adr/README.md)** | the ADRs: why the API looks the way it does |
| **[The test map](./docs/testing.md)** | which test proves which rule |
| **[The alignment](./docs/architecture/alignment.md)** and **[the domain](./docs/domain/lui.md)** | which rules of the standards apply to a library — and the terms the library speaks |

## Status

LUI is **feature-complete for 0.1.0 and not yet published**. The design is specified in the
[`specification`](./docs/spec/LUI-Spezifikation.md), the contract of this repository; every decision is an ADR in [`docs/adr/`](./docs/adr/).

Two packages, one version: `Nordmann.LUI` (the library) and `Nordmann.LUI.Hosting` (the dependency injection)
([ADR-0018](./docs/adr/0018-two-packages-core-and-hosting.md)). The core takes `Microsoft.Data.Sqlite` and
`symspell` and nothing else ([ADR-0004](./docs/adr/0004-dependencies.md)); the target framework is `net10.0`, the licence
is MIT, and the public API grows only additively from `0.1.0` on ([ADR-0026](./docs/adr/0026-the-public-api-from-0-1-0-on-grows-only-additively.md)).

## Working on LUI

Read [`AGENTS.md`](./AGENTS.md) first — it is the contract for this repository, and it inherits
[`nordmanndev/standards`](https://github.com/nordmanndev/standards).

- **One issue, one pull request**, each with its tests, its measurement and — for a decision that changes the public
  API, the schema or the dependencies — its ADR.
- **The specification is the contract.** Code that contradicts it is a bug, unless an ADR says the specification is
  wrong; then both change in the same pull request.
- **Before a pull request:** `dotnet test`, `dotnet format --verify-no-changes`, `tools/package-smoke-test.sh`,
  `tools/mutation-check.sh` — the test map ([`docs/testing.md`](./docs/testing.md)) is a guard: an unlisted test file fails the build.
- **The code of this README runs.** Every C# block above is executed by the package smoke test, against the packed
  `.nupkg` — so the README cannot drift from the code.

## Licence

MIT — see [`LICENSE`](./LICENSE).
