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

LUI is **under construction**. The design is settled and specified
([`docs/spec/LUI-Spezifikation.md`](./docs/spec/LUI-Spezifikation.md)); the
implementation follows the plan in
[`docs/plan/2026-11-02-lui-repo-plan.md`](./docs/plan/2026-11-02-lui-repo-plan.md),
one pull request per step.

| | |
|---|---|
| Package id | `Nordmann.LUI` (one package — [ADR-0001](./docs/adr/0001-single-package.md)) |
| Target framework | `net10.0` |
| Licence | MIT |
| Current version | `0.1.0` (not yet published) |

The public API follows the specification: a `LuiCatalog`, an index per
searchable stock, a fluent query builder, and the two index kinds
`SingleWord` and `MultiWord`. The quick start below is **executed by the
package smoke test** — every C# block of this README is — so the README can
never drift from the code.

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

| | `Owned` (the default) | `Linked` |
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
    .AddIndex("tags", index => index.SingleWord().LinkTo(table: "tag", key: "id", text: "name", weight: "usage_count"),
              applyPendingEvery: TimeSpan.FromSeconds(5))
    .WarmUpTypoDictionariesOnStart();

using var host = builder.Build();
host.Start();

// in an endpoint: [FromKeyedServices("recipes")] ILuiIndex<long, LuiNoPayload> recipes
var suggest = host.Services.GetRequiredKeyedService<ILuiIndex<long, LuiNoPayload>>("recipes");
Console.WriteLine(suggest.Search("apfelkuch").Hits[0].Text);   // Gedeckter Apfelkuchen
```

Without a host — in a console application — the same is three calls of your
own: `Ensure()` where you open an index, `WarmUpTypoDictionary()` at the start,
and `ApplyPending()` after your writes or in a `PeriodicTimer` loop.

---

## Repository layout

```text
src/Nordmann.LUI/            ← the library (package Nordmann.LUI)
src/Nordmann.LUI.Hosting/    ← the dependency injection (package Nordmann.LUI.Hosting, same version)
tests/                       ← unit, architecture and integration tests
                              (and the map of them: docs/testing.md)
benchmarks/                  ← BenchmarkDotNet + the corpus generator
samples/Lui.Sample/          ← ASP.NET Core API + the inline static site
docs/adr/                    ← the decisions, and why
docs/spec/                   ← the specification this repo implements
docs/plan/                   ← the delivery plan
```

## Working on LUI

Read [`AGENTS.md`](./AGENTS.md) first — it is the contract for this
repository, and it inherits
[`nordmanndev/standards`](https://github.com/nordmanndev/standards).

## Licence

MIT — see [`LICENSE`](./LICENSE).
