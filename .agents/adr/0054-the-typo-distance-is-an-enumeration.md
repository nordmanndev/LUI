# 0054 — The typo distance is an enumeration, and it belongs to the index

- **Status:** Accepted
- **Date:** 2026-11-11
- **Builds on:** [0012](./0012-typo-tolerance.md) (the typo tolerance),
  [0013](./0013-the-api-shape-before-the-first-release.md) (the bool of the builder),
  [0021](./0021-a-stale-typo-dictionary-and-a-query-that-does-not-wait.md) (the dictionary and its memory),
  [0025](./0025-the-typo-dictionary-only-grows.md) (one dictionary per index),
  [0026](./0026-the-public-api-from-0-1-0-on-grows-only-additively.md) (the freeze begins with the tag `v0.1.0`)
- **Affects:** `src/Nordmann.LUI/LuiTypoDistance.cs` (the new type), `src/Nordmann.LUI/ILuiIndex.cs`,
  `src/Nordmann.LUI/LuiEmbeddedIndexBuilder.cs`, `src/Nordmann.LUI/LuiLinkedIndexBuilder.cs`,
  `src/Nordmann.LUI/Schema/IndexOptions.cs`, `src/Nordmann.LUI/Schema/IndexDefinition.cs`,
  `src/Nordmann.LUI/LuiCatalog.cs`, `src/Nordmann.LUI/Typos/TypoDictionary.cs`,
  `src/Nordmann.LUI/Typos/TypoDictionaries.cs`, `src/Nordmann.LUI/Querying/QueryRunner.cs`,
  `samples/Lui.Sample/`, `docs/api.md`, `docs/typo-dictionary.md`, `docs/testing.md`, `CHANGELOG.md`
- **Issue:** #118, step 1 of #117. The owner's decision of 2026-11-11, in the plan
  [`.agents/plan/2026-11-11-the-public-surface-and-the-measurement.md`](../plan/2026-11-11-the-public-surface-and-the-measurement.md).

## Context

The typo tolerance of ADR-0012 forgives a typo. ADR-0013 turned the number of the builder into a bool, because "more
than one edit is not supported" — and the dictionary was built with `maxDictionaryEditDistance: 1` hardcoded inside
[`TypoDictionary`](../../src/Nordmann.LUI/Typos/TypoDictionary.cs). Two things were wrong with that:

- **The distance was a number nobody could choose.** The dictionary keeps the *delete variants* of SymSpell, and their
  number grows with the distance: one error needs the deletes of one deletion, two errors need the deletes of two. A
  free number would be a promise the library cannot keep — and the cost of every step was unmeasured, so nobody could
  say what the next step would cost.
- **The distance was lost at the next start.** It was a property of the *builder*: an index that was opened again
  (`LuiCatalog.Index(name)`) was handed `typoTolerance: false`, so an index that forgave typos stopped forgiving them
  after a restart — unless the query asked for the correction again. The setting was in the builder and not in the
  stored definition, so the file could not say what its index was.

A third thing was measured on the way: the dictionary of 685,829 words costs **138 MB** and **4.2 s** — a price an
application pays for a correction it may never need. "Off" was a bool, so "off" was the only answer; a distance of
*none* would have been the better word for it.

## Decision

**The distance is an enumeration, `LuiTypoDistance`, and it belongs to the index.**

- **`LuiTypoDistance` is a public type with three values:** `None`, `OneError`, `TwoErrors`. An enumeration and not a
  number: the possible distances are the ones that were measured, the numbers stand in
  [`docs/typo-dictionary.md`](../../docs/typo-dictionary.md), and a caller cannot ask for a tenth.
- **The builder takes the distance:** `TypoTolerance(LuiTypoDistance distance = LuiTypoDistance.OneError)`. The bool
  overload is gone. It is a breaking change, and it is still free: the freeze of ADR-0026 begins with the tag `v0.1.0`,
  and there is no such tag yet.
- **The query keeps the bool:** `Query(text).TypoTolerance(bool)` and `LuiQueryOptions.TypoTolerance` are unchanged. A
  query switches the correction on or off; the *distance* belongs to the index, because the dictionary is built with it.
- **The distance is part of the stored definition** (`IndexDefinition.TypoDistance`). An index that is opened again
  knows how its dictionary was built — and an index that forgives typos forgives them after a restart too. A definition
  written before this field carries `None`: an index that corrected nothing.
- **`None` builds no dictionary at all.** The state of the dictionary stays `NotBuilt`, and the memory of one is not
  taken. The proof is deterministic: the source of the dictionary is never read.
- **An index that exists with another distance is a `LuiDefinitionMismatchException`** — the distance is part of the
  definition, and the comparison of the definitions (ADR-0033) already covers it. An application that creates its
  indexes in its migration describes an index the same way every time; a change of the distance wants a
  `DropIndex(name)` and a new index.

## What was measured

The numbers of the dictionary, measured on the word list of the repository (685,829 words, the list that
[`tools/fetch-wordlist.sh`](../../tools/fetch-wordlist.sh) fetches), on 2026-11-11, with
`LUI_PERF=1 dotnet test tests/Nordmann.LUI.Integration.Tests --filter "FullyQualifiedName~TypoPerformance.What_every_distance_costs"`:

<!--MEASURED-->
| Distance | The dictionary | The build | The memory | The budget of §16 |
|---|---|---|---|---|
| `None` | not built | 0.0 s | **0 MB** | — (nothing to build) |
| `OneError` | built | 4.1 s | **138 MB** | ≤ 200 MB, ≤ 10 s — **held** |
| `TwoErrors` | built | 8.6 s | **240 MB** | ≤ 200 MB, ≤ 10 s — **the memory is missed** |

Two errors cost 1.7 times the memory of one and twice the time. The budget of §16 was written for one error, and it
holds for one error — the two-error distance needs a new budget of its own before the tag `v0.1.0`.

The one-error row is the number of ADR-0021 — the same word list, the same 138 MB — and the run is the proof that the
measurement is comparable with the old one.

## Consequences

- **`TypoTolerance(false)` is gone.** `TypoTolerance(LuiTypoDistance.None)` says it, and the default of the builder is
  `OneError` — the behaviour of the old `TypoTolerance()` is unchanged.
- **An application that reads `ILuiIndex` itself** has to implement `TypoDistance` instead of `TypoTolerance`.
- **An index that is opened again corrects now.** Before this change an opened index never corrected unless the query
  asked for it. The change is visible in the behaviour, not only in the API.
- **The CHANGELOG carries the migration note**, because a breaking change needs one (ADR-0026).
- **The mutation check has three new mutations** — the distance that does not survive a restart, the distance that never
  reaches the dictionary, and the correction that always forgives one error. Every one of them has to turn the build
  red.
- **The sample shows the distance** instead of a bool: the panel of the indexes names the distance of the index and the
  state of its dictionary.

## Alternatives

- **A free number, `TypoTolerance(int maxEdits)`.** Rejected: the caller could ask for a distance the dictionary cannot
  answer, and the cost of every step is unmeasured. ADR-0013 already took the number away for exactly this reason.
- **The distance stays in the builder.** Rejected: an index that is opened again would forget it, and the file could not
  say what its index was. The definition is the place where "what this index is" is written down.
- **The query takes the distance too.** Rejected: the dictionary is built once, with the distance of the index. A query
  that asks for a distance the dictionary was not built with would be a promise the library cannot keep — the same
  mistake as the free number, one level down.
- **`None` keeps building a dictionary.** Rejected: 138 MB and 4.2 s for a correction that is never made. The state of
  the dictionary stays `NotBuilt` instead.
