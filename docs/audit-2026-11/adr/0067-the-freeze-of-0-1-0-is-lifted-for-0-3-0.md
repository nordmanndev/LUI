# 0067 — The freeze of 0.1.0 is lifted for 0.3.0

- **Status:** Proposed
- **Date:** 2026-11-14
- **Supersedes:** [0026](./0026-the-public-api-from-0-1-0-on-grows-only-additively.md)
- **Builds on:** [0063](./0063-the-surface-of-0-1-0-is-the-review-of-2026-11-04-applied.md) (the surface of 0.1.0),
  [0064](./0064-the-catalogue-owns-the-connection.md) (the catalogue owns the connection),
  [0065](./0065-the-read-path-opens-a-read-only-connection.md) (the read path opens a read-only connection),
  [0066](./0066-the-open-of-a-catalogue-writes-nothing.md) (the open of a catalogue writes nothing)
- **Affects:**
  - `.agents/adr/0026-…` — the status becomes "Superseded by 0067"
  - `.agents/adr/README.md` — the index
  - `CHANGELOG.md` — the entry of the release, with the migration note
  - `docs/api.md` — the members that move
- **Issue:** the epic of 0.3.0

## Context

**[0026](./0026-the-public-api-from-0-1-0-on-grows-only-additively.md) freezes the public API:** from 0.1.0 on, the API
grows and never shrinks. The freeze has held — 0.1.1 changed no member.

**The work of 0.3.0 breaks it in three named places**, and the owner has decided that the work happens and that the
version is 0.3.0:

| What moves | From | To |
|---|---|---|
| the busy timeout | `LuiCatalogOptions.BusyTimeout` | `LuiCatalogOptions.Pragmas.BusyTimeout` |
| the page cache | `LuiCatalogOptions.CacheSizeMegabytes` | `LuiCatalogOptions.Pragmas.CacheSizeMegabytes` |
| the memory map | `LuiCatalogOptions.MemoryMapMegabytes` | `LuiCatalogOptions.Pragmas.MemoryMapMegabytes` |
| the journal mode | `LuiCatalogOptions.WriteAheadLog` (a boolean) | **gone** — the journal mode is not a parameter |

Two new public types come with it: `LuiPragmas` and the enumeration `LuiSynchronous`.

**Why the freeze may not hold for this work:** the freeze protects the users of a published API. The package has not
been published to nuget.org — the review of 2026-11-04 said so and the owner repeated it in
[0063](./0063-the-surface-of-0-1-0-is-the-review-of-2026-11-04-applied.md). **The users of LUI today are the tests, the
benchmarks and the sample**, and they move with the change.

## Decision

**The freeze of 0.1.0 is lifted for 0.3.0 and holds again from 0.3.0 on.**

1. The API may break in the four places named above — and only there. Every other member of the public API of 0.1.1
   stays as it is.
2. The breaking changes are **mechanical**: three members move from one type to another, and one member goes. The
   migration note in the changelog names every one of them with its old and its new name.
3. **From 0.3.0 on, the freeze holds again** — the API grows and never shrinks.
4. The records of 0.1.0 stay as they are written. The points that [0026](./0026-the-public-api-from-0-1-0-on-grows-only-additively.md)
   declined stay declined; the freeze was not an argument for them.

## Consequences

- **The changelog of 0.3.0 carries a migration note** — the four members and their new place, with a before and after
  that compiles.
- **The API page names the new type** and the members that moved.
- **The tests, the benchmarks and the sample are updated in the same pull request** — they are the users of the API
  that moves.
- **The version is 0.3.0**, not 0.2.0: the change is not only additive, and the release that breaks an API is named
  after the minor part.
