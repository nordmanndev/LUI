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

The public API follows the specification: a `LuiCatalog`, an `ILuiIndex` per
searchable stock, a fluent query builder, and the two index kinds
`SingleWord` and `MultiWord`. The quick start lands with the implementation —
and it is **executed by the build**, so the README can never drift from the
code.

## Install

```text
dotnet add package Nordmann.LUI
```

The package is not on nuget.org yet. The publish pipeline is in place and
waits for its tag.

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

| | `Owned` | `Linked` |
|---|---|---|
| Content table | `lui_<index>` — the library's own | a table of your application |
| Who writes | the application, via `Upsert` / `Remove` / `Sync` | the application, with ordinary SQL |
| Index ↔ content | consistent by triggers | consistent by triggers |
| Text stored twice | yes | no |

Both use the same mechanism, so there is one thing to learn, not two.

---

## Repository layout

```text
src/Nordmann.LUI/            ← the package (one assembly, one package)
tests/                       ← unit, architecture and integration tests
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
