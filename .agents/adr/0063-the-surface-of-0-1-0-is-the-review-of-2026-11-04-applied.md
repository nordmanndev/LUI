# 0063 — The surface of 0.1.0 is the review of 2026-11-04, applied

- **Status:** Accepted
- **Date:** 2026-11-13
- **Builds on:** [0023](./0023-owned-becomes-embedded.md) (`Owned` → `Embedded`), [0024](./0024-the-api-polish-before-the-freeze.md)
  (the polish before the freeze), [0025](./0025-the-typo-dictionary-only-grows.md) (the typo dictionary grows only),
  [0026](./0026-the-public-api-from-0-1-0-on-grows-only-additively.md) (the freeze),
  [0033](./0033-the-read-path-opens-an-index-without-writing.md) (the read path opens an index without writing),
  [0035](./0035-the-index-knows-no-types.md) (the index knows no types),
  [0044](./0044-the-typed-view-over-the-untyped-index.md) (the typed view),
  [0054](./0054-the-typo-distance-is-an-enumeration.md) (the typo distance is an enumeration),
  [0055](./0055-the-uniqueness-of-a-text-belongs-to-the-data.md) (the uniqueness of a text belongs to the data),
  [0062](./0062-the-creation-names-the-route-create-and-createlinked.md) (the creation names the route)
- **Affects:**
  - `.agents/adr/0026-…` — a dated note: the row of A.2 in its table names a type that is gone
  - `.agents/adr/README.md` — the index
  - `CHANGELOG.md` — the entry of the release
  - `docs/api.md` — **not changed**: it stays the member-by-member list, and this record points at it
- **Issue:** #144 (epic #117, and the closing of #39)

## Context

On 2026-11-04 an independent architecture and code-quality review of `main` @ `6d6d84a` found the same thing the work
acts on: **the API has never been published.** Neither package exists on nuget.org, so a breaking change costs nothing
today and a great deal after 0.1.0. The owner went through the review point by point and decided what LUI would do
before the freeze (epic #39).

The decisions are written down — [0023](./0023-owned-becomes-embedded.md), [0024](./0024-the-api-polish-before-the-freeze.md),
[0025](./0025-the-typo-dictionary-only-grows.md), [0026](./0026-the-public-api-from-0-1-0-on-grows-only-additively.md) — and
[0026](./0026-the-public-api-from-0-1-0-on-grows-only-additively.md) lists the points of the review that were **declined**, by
name, so that a later contributor cannot take one of them up and break the promise.

Three things were still missing:

1. **The other half of the review.** The points that were **decided** are in the records of their decision, but a reader
   of 0.1.0 cannot tell which member of the package a point of the review produced. The surface of a library is what its
   users call; the review is why it is shaped that way. Nothing connects the two.
2. **What happened after the review.** Four points of the review were taken up again. Three were decided differently,
   because the world of the library moved: the catalog became the entry point
   ([0033](./0033-the-read-path-opens-an-index-without-writing.md)), the index learned no types
   ([0035](./0035-the-index-knows-no-types.md)), and the typed view replaced the untyped facade
   ([0044](./0044-the-typed-view-over-the-untyped-index.md)). One — **A.3**, the route named in the call — was decided the
   way the review wanted it, in the place the role moved to: `LuiCatalog.Create` and `LuiCatalog.CreateLinked`
   ([0062](./0062-the-creation-names-the-route-create-and-createlinked.md)). Two changes came *after* the review entirely:
   the typo distance became an enumeration ([0054](./0054-the-typo-distance-is-an-enumeration.md)) and the uniqueness of a
   text was given to the data ([0055](./0055-the-uniqueness-of-a-text-belongs-to-the-data.md)). The review could not have
   asked for any of the six; the table of the surface has to carry them, or the surface of 0.1.0 is not the surface that is
   published.
3. **A row of the freeze that had become untrue.** The table of the declined points in
   [0026](./0026-the-public-api-from-0-1-0-on-grows-only-additively.md) says of A.2: *"`LuiServicesBuilder` →
   `LuiIndexRegistrations` — **Not done.** The name stays."* The name does not stay: the type is gone. The container
   holds the catalog and the warm-up, and the builder of the indexes went with them
   ([0033](./0033-the-read-path-opens-an-index-without-writing.md)); a guard says so
   (`The_container_holds_the_catalog_and_the_warm_up_but_no_index`). The decision of the owner still holds — there is no
   `LuiIndexRegistrations` either — but the sentence that carries it names a type that no longer exists.

## Decision

**The public surface of 0.1.0 is the review of 2026-11-04, applied — and the table below is that surface, written against
the review.** Every point of the review has a line; every line names the type or the member that carries it. The
member-by-member list stays where it is — [`docs/api.md`](../../docs/api.md) — and this record says where each part of it
comes from.

### The points that were decided

| # | What the review asked | Where it stands in 0.1.0 | The record |
|---|---|---|---|
| C.1 | one internal settings record, so that a builder that becomes another builder passes every setting on | `IndexOptions` — one internal record for the similarity, the transliteration and the typo distance. The test: `Every_step_that_changes_the_key_or_the_payload_keeps_every_setting` | [0024](./0024-the-api-polish-before-the-freeze.md) (context) |
| C.2 | `IndexRegistration` split into an embedded and a linked registration | **Moot** — the registrations are gone with the entry point: the container holds the catalog and the warm-up, and no index | [0033](./0033-the-read-path-opens-an-index-without-writing.md) |
| C.3 | one loop for the two hosted services | **Moot** — the second service is gone; one hosted service remains (`LuiWarmupService`, internal) | [0025](./0025-the-typo-dictionary-only-grows.md) |
| A.1 | `Owned` → `Embedded` | `ILuiEmbeddedIndex`, `LuiEmbeddedIndexBuilder`, `ILuiLinkedIndex`, `LuiLinkedIndexBuilder` — the old word is gone from the public surface | [0023](./0023-owned-becomes-embedded.md) |
| A.3 | `AddIndex` / `AddLinkedIndex` instead of two overloads chosen by the lambda | the route is named in the call — in the catalog, which took the role of the builder over: `LuiCatalog.Create` creates an embedded index, `LuiCatalog.CreateLinked` a linked one | [0024](./0024-the-api-polish-before-the-freeze.md) → [0033](./0033-the-read-path-opens-an-index-without-writing.md) → [0062](./0062-the-creation-names-the-route-create-and-createlinked.md) |
| A.4 | `LuiPriorityRanking` → `LuiPriorityMode`, `.Priorities(…)` → `.PriorityMode(…)` | `LuiPriorityMode`, `LuiQuery.PriorityMode(…)`, `LuiQueryOptions.PriorityMode` | [0024](./0024-the-api-polish-before-the-freeze.md) |
| A.5 | `LuiTimings.Candidates` → `CandidateRead` — a duration, not a budget | `LuiTimings.CandidateRead` | [0024](./0024-the-api-polish-before-the-freeze.md) |
| A.6 | the parameter `source` → `entries` in `Sync` and `Check` | `ILuiEmbeddedIndex.Sync(IEnumerable<LuiEntry> entries, …)`, `ILuiEmbeddedIndex.Check(IEnumerable<LuiEntry> entries, …)` | [0024](./0024-the-api-polish-before-the-freeze.md) |
| A.7 | `LuiRow` → `LuiPayloadRow`, and a kept row throws | `LuiPayloadRow` — it forgets its reader when the mapping returns | [0024](./0024-the-api-polish-before-the-freeze.md) |
| A.8 / D.6 | `LuiException` as the base of the exceptions | `LuiException : InvalidOperationException`, abstract; six exceptions derive from it | [0024](./0024-the-api-polish-before-the-freeze.md) |
| B.1 | `LuiSyncResult` → `LuiLinkedSyncReport` — one vocabulary, two types | `LuiSyncReport` (the embedded index compares with a source), `LuiLinkedSyncReport` (the linked index rebuilds from a table) | [0024](./0024-the-api-polish-before-the-freeze.md) |
| B.2 | no `With…` methods; a guard instead | `LuiQueryOptions` has none — a record has `with`. The guard: `Every_option_of_a_query_can_be_set_on_the_query_and_every_setting_of_the_query_is_an_option` | [0024](./0024-the-api-polish-before-the-freeze.md) |
| B.3 | `WarmUpTypoDictionary`, `RebuildTypoDictionary` and `Check()` leave the read interface | `ILuiIndex` reads (`Name`, `Kind`, `KeyKind`, `TypoDistance`, `TypoDictionaryState`, `Count`, `Query`, `Search`); the maintenance is on `ILuiEmbeddedIndex` and `ILuiLinkedIndex` | [0024](./0024-the-api-polish-before-the-freeze.md) |
| B.4 | `ILuiSimilarity.Name` removed | `ILuiSimilarity` has no `Name` | [0024](./0024-the-api-polish-before-the-freeze.md) |
| D.4 | the README says that `AddLui` opens the database when the host starts | the README, "In an ASP.NET Core application": `AddLui` puts the catalog into the container — and nothing else. The switch `EnsureOnStart` is gone: the file decides what may be written | [0024](./0024-the-api-polish-before-the-freeze.md), [0033](./0033-the-read-path-opens-an-index-without-writing.md) |
| G | the freeze | the public API grows only additively from the tag `v0.1.0` on | [0026](./0026-the-public-api-from-0-1-0-on-grows-only-additively.md) |
| owner, 2026-11-05 | the read path gets its own entry: `Open()` reads the definition and writes nothing, `Ensure()` is renamed `Create()`, an untyped facade for `Open`, the payload columns go into the definition | **Three of the four stand.** `LuiCatalog.Open` writes nothing; `LuiCatalog.Create` (was `Ensure`); `catalog.Index(…)` and `catalog.LinkedIndex(…)` open an index; the payload columns are in the definition (`LinkDefinition.PayloadColumns`). **The untyped facade is not part of the API** — one implementation path is worth more than the convenience | [0033](./0033-the-read-path-opens-an-index-without-writing.md), [0035](./0035-the-index-knows-no-types.md), [0044](./0044-the-typed-view-over-the-untyped-index.md) |

#### A.3 — the route, named in the call

The review wanted the route **named in the call**: `AddIndex` for an embedded index, `AddLinkedIndex` for a linked one —
because a linked description handed to the wrong method should not compile. The role of the builder of the indexes went
with the entry point ([0033](./0033-the-read-path-opens-an-index-without-writing.md)), and the creation moved into the
core — at first with the shape the review wanted to stop: two overloads of `Create`, the compiler choosing by the return
type of the lambda. The owner decided on 2026-11-13 that the route is named in the call again
([0062](./0062-the-creation-names-the-route-create-and-createlinked.md)): `Create` creates an embedded index,
`CreateLinked` a linked one. The naming is complete on the side of opening as well: `catalog.Index(name)` and
`catalog.LinkedIndex(name)`.

### The points that were declined

The reasons are the owner's, of 2026-11-04; they stand where [0026](./0026-the-public-api-from-0-1-0-on-grows-only-additively.md)
carries them. What follows is where each of them stands in the code today.

| # | The proposal | The decision | Where it stands in 0.1.0 |
|---|---|---|---|
| A.2 | `LuiServicesBuilder` → `LuiIndexRegistrations` | not done — the name stays | **The type is gone.** There is no builder of the indexes: the container holds the catalog and the warm-up. The guard `The_container_holds_the_catalog_and_the_warm_up_but_no_index` says so |
| A.4 | `LuiPriority.Elevated` → `Medium` | not done | `LuiPriority`: `System` = 0, `High` = 1, `Elevated` = 2, `Normal` = 3 |
| B.1 | one sync type for both routes | not done | `LuiSyncReport` and `LuiLinkedSyncReport` — two types |
| B.2 | `With…` methods on `LuiQueryOptions` | not done | none |
| C.4 / C.5 | keyed registration of the internal options and registrations | not done | three registrations, all named `AddLui` or `AddLuiWarmup`; none keyed |
| D.2a | name the hosting switches in the README | done since #19 | `AddLui`, `AddLuiWarmup` |
| D.3 | end `LuiTypoRebuilder` when there is nothing to do | moot | the service is gone ([0025](./0025-the-typo-dictionary-only-grows.md)) |

### The changes that came after the review

Two decisions of the series changed the surface after the review of 2026-11-04. The review could not have asked for them;
the table of the surface has to carry them, or the surface of 0.1.0 is not the surface that is published.

| Change | What it did to the surface | Where it stands in 0.1.0 |
|---|---|---|
| [0054](./0054-the-typo-distance-is-an-enumeration.md) (2026-11-12) | the typo distance is an enumeration, and it belongs to the index | `LuiTypoDistance` (`None`, `OneError`, `TwoErrors`); `LuiEmbeddedIndexBuilder.TypoTolerance(LuiTypoDistance distance = LuiTypoDistance.OneError)`; `ILuiIndex.TypoDistance`. The query keeps its bool: `LuiQuery.TypoTolerance(bool)` |
| [0055](./0055-the-uniqueness-of-a-text-belongs-to-the-data.md) (2026-11-12) | the uniqueness of a text belongs to the data | `LuiQueryOptions.DistinctText` and `LuiQuery.DistinctText(bool)` are gone |

### What the tag promises

The tag `v0.1.0` begins the freeze of [0026](./0026-the-public-api-from-0-1-0-on-grows-only-additively.md): from it on, the
public API grows only additively. A new type, a new method, a new overload, a new `init` property — that is free. A
removed or renamed member, a changed signature, a changed meaning — that is `0.2.0` with a migration note and an ADR.

**The surface of 0.1.0 is the table above.** It is not the review: the review is a review of a day, and four of its
points were taken up again afterwards — three of them decided differently, one of them the way the review wanted it.

## Consequences

- **The surface has a story.** A reader of [`docs/api.md`](../../docs/api.md) who asks *why* a member looks like this finds
  the row that carries it, and the record behind the row. A contributor who wants to change it finds the rule of the
  freeze in the same place.
- **A point of the review that was decided differently is not lost.** It stands in the table with the decision that took
  it away and the reason — the untyped facade of the owner's addition of 2026-11-05 is the example: the review asked for
  it, [0035](./0035-the-index-knows-no-types.md) did not deliver it, and the reader of 0.1.0 sees both.
- **[0026](./0026-the-public-api-from-0-1-0-on-grows-only-additively.md) gets a dated note.** Its row of A.2 named a type
  that is gone; the note says so, and the table above carries the truth. The record is history — it is not rewritten to
  look as if it had always been written this way.
- **The member-by-member list stays in one place.** [`docs/api.md`](../../docs/api.md) is the API; this record is the
  provenance. The documentation of the library and the papers of the work stay apart, as
  [`docs/README.md`](../../docs/README.md) says.
- **The freeze is checked by review, not by a gate.** There is no snapshot of the public API (the owner declined it on
  2026-11-04); the guards of the shapes hold the lines they hold, and this table says what a change would break.

## Alternatives

| | For | Against |
|---|---|---|
| **A — the table in this record** (this decision) | one place says which point of the review produced which member of 0.1.0, and which point was decided differently afterwards | the table is a second list of the surface, and it can drift from the code — the members it names are the proof, and the review of a pull request reads them |
| **B — a page under `docs/`** | the reader of the package sees the provenance next to the API | the documentation of the library is about the library; the review is a paper of the work, and `docs/README.md` says that the papers are not part of it |
| **C — a note in `docs/api.md` per type** | the provenance sits at the member | the API page would carry the decisions of the work, and a member with two names would carry two stories |
| **D — nothing, the records of the decisions are enough** | one record per decision, no summary | the surface of 0.1.0 could not be read against the review, and the row of A.2 in the freeze stayed untrue for a reader who did not know the catalog had taken the role |
