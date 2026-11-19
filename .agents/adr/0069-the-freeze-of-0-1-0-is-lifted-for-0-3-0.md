# 0069 — The freeze of 0.1.0 is lifted for 0.3.0

- **Status:** Accepted (owner, 2026-11-15)
- **Date:** 2026-11-14, revised 2026-11-15 (the breaks of the decisions of the 15th; the reason corrected — the package is published)
- **Supersedes:** [0026](./0026-the-public-api-from-0-1-0-on-grows-only-additively.md)
- **Builds on:** [0063](./0063-the-surface-of-0-1-0-is-the-review-of-2026-11-04-applied.md) (the surface of 0.1.0),
  [0066](./0066-the-catalogue-owns-the-connection.md) (the catalogue owns the connection),
  [0067](./0067-the-read-path-opens-a-read-only-connection.md) (the read path opens a read-only connection),
  [0068](./0068-three-ways-to-open-a-catalogue.md) (three ways to open a catalogue)
- **Affects:**
  - `.agents/adr/0026-…` — the status becomes "Superseded by 0069"
  - `.agents/adr/README.md` — the index
  - `Directory.Build.props` — the version is 0.3.0
  - `CHANGELOG.md` — the entry of the release, with the migration note
  - `docs/api.md` — the members that move
  - `tests/Nordmann.LUI.Architecture.Tests/ReleaseThreeTests.cs` — the guard of the shape of the API of 0.3.0
- **Issue:** [#172](https://github.com/nordmanndev/LUI/issues/172), the epic of 0.3.0 ([#166](https://github.com/nordmanndev/LUI/issues/166))

## Context

**[0026](./0026-the-public-api-from-0-1-0-on-grows-only-additively.md) freezes the public API:** from 0.1.0 on, the API
grows and never shrinks. The freeze has held — 0.1.1 and 0.2.0 changed no member.

**The work of 0.3.0 breaks it in named places**, and the owner has decided that the work happens and that the version is
0.3.0:

| What | From (0.1.x, 0.2.x) | To (0.3.0) |
|---|---|---|
| the open | `LuiCatalog.Open(string connectionString, LuiCatalogOptions?)` | `Open(string path, …)`; new: `OpenOrCreate`, `OpenReadOnly` |
| the place of the catalogue | `LuiCatalog.ConnectionString` | `LuiCatalog.Path` |
| the busy timeout | `LuiCatalogOptions.BusyTimeout` | `LuiCatalogOptions.Pragmas.BusyTimeout` |
| the page cache | `LuiCatalogOptions.CacheSizeMegabytes` | `LuiCatalogOptions.Pragmas.CacheSizeMegabytes` |
| the memory map | `LuiCatalogOptions.MemoryMapMegabytes` | **gone** — measured: no gain |
| the journal mode | `LuiCatalogOptions.WriteAheadLog` (a boolean) | **gone** — the journal mode is not a parameter |
| the host | `AddLui(string, LuiCatalogOptions?)`, `AddLui(Func<IServiceProvider, string>, …)` | `AddLui(Func<IServiceProvider, LuiCatalog>)` |

**Two breaks the compiler does not see:** `Open` no longer creates or sets up a file (`OpenOrCreate` does), and read-only
is the method `OpenReadOnly` — there is no connection string left to say `Mode=ReadOnly`.

Three new public types come with it: `LuiPragmas`, `LuiSynchronous` and `LuiCatalogNotFoundException`.

**The package is published.** 0.1.0, 0.1.1 and 0.2.0 are on nuget.org; the freeze protects real users. The record of
2026-11-14 said otherwise — that was true before the release and is not any more.

## Decision

**The freeze of 0.1.0 is lifted for 0.3.0 and holds again from 0.3.0 on.**

1. The API may break in the places named above — and only there. Every other member of the public API of 0.2.0 stays as it
   is. A guard of the architecture tests holds the shape of the API of 0.3.0 against the table.
2. **The reason is not the absence of users.** It is that the version is 0.x — Semantic Versioning allows any change
   there, and the freeze was LUI's own promise beyond it; that the published API is days old; that every break repairs a
   fault of the published API that was measured after the release (the open that writes and creates, the settings that
   break the budgets, the memory map that buys nothing); and that every break has its way back in the migration note.
3. The migration note in the changelog names every break with its old and its new form — **the two the compiler does not
   see come first**.
4. **From 0.3.0 on, the freeze holds again, with the rule of 0026**: the public API grows and never shrinks; a breaking
   change — a removed or renamed member, a new optional parameter on an existing method, a new abstract member of an
   interface, a changed enum value, a new positional parameter of a record — needs a new minor version, a migration note in
   `CHANGELOG.md` and a record of its own. Another lifting needs a record of its own, too.
5. The records of 0.1.0 stay as they are written. The points that [0026](./0026-the-public-api-from-0-1-0-on-grows-only-additively.md)
   declined stay declined; the freeze was not an argument for them.

## Consequences

- **The changelog of 0.3.0 carries a migration note** — every break and its new form, with a before and after that compiles,
  and the two behavioural breaks first.
- **An application on 0.1.x or 0.2.x that upgrades** gets compile errors for every moved member — and, if it relied on
  `Open` creating the file, an exception at its first start that names `OpenOrCreate`.
- **An application that encrypts its database or uses a VFS of its own cannot upgrade** — deliberately; the migration note
  says so.
- **The API page names the new types** and the members that moved.
- **The tests, the benchmarks, the sample, the tutorial and the README are updated** with the changes that move the API
  (#169, #171) — the guard of the shape of the API holds the result.
- **The version is 0.3.0**, not 0.2.0: the change is not only additive.
