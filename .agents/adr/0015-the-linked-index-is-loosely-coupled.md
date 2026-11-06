# 0015 — The linked index is loosely coupled: LUI never changes the application's table on its own

> **Name note (ADR-0023, 2026-11-04):** since this record was written, `Owned` is called `Embedded`: `ILuiOwnedIndex` is
> `ILuiEmbeddedIndex`, `LuiIndexBuilder` is `LuiEmbeddedIndexBuilder`, and `OwnedIndex`, `OwnedSource`, `OwnedSync` and
> `OwnedCheck` are `Embedded…`. The text below is history and stays as it was written.

> **Name note (ADR-0024, 2026-11-04):** since this record was written, `LuiRow` is `LuiPayloadRow`, `LuiSyncResult` is
> `LuiLinkedSyncReport`, `LuiPriorityRanking` is `LuiPriorityMode` (`Priorities` → `PriorityMode`), `LuiTimings.Candidates`
> is `CandidateRead`, the linked overload of `AddIndex` is `AddLinkedIndex`, and `Check()`, `WarmUpTypoDictionary` and
> `RebuildTypoDictionary` are on the write interfaces, not on `ILuiIndex`. The text below is history.

- **Status:** Accepted
- **Date:** 2026-11-03
- **Supersedes:** the `Linked` design of the specification, draft 2 (§5, §7.3, §12.2: generated columns, indexes and
  triggers on the application's table behind `allowSchemaChange`); the source step of the builder in ADR-0013
- **Affects:** `src/Nordmann.LUI/Schema/LinkedSource.cs`, `Schema/ContentSources.cs`, `Schema/LinkDefinition.cs`,
  `Indexes/LinkedIndex.cs`, `Indexes/LinkedTable.cs`, `Querying/ColumnPayload.cs`, `LuiLinkedIndexBuilder.cs`,
  `LuiIndexBuilderExtensions.cs`, `ILuiLinkedIndex.cs`, `LuiRow.cs`, `LuiLinkedTableException.cs`, `LuiIndexKindBuilder.cs`,
  `LuiIndexBuilder.cs`; `.agents/spec/LUI-Spezifikation.md` §5, §7.3, §12, §14, §20; `AGENTS.md`; `README.md`

## Context

A `Linked` index searches a table of the application (spec §5). Draft 2 of the specification gave that table five
generated columns, three indexes and three triggers, created by `Ensure()` with `allowSchemaChange: true`. The owner
decided against it (issue #15): *the main schema of the application must not be cluttered — no columns; triggers only
if it must be, and then not implicitly; the responsibility for filling the index may lie with the application, which
can still choose to set triggers.* And: the transaction of a refresh must be optional.

So the question was whether an index can live next to a table without a column of it, and without storing its text a
second time.

## Decision

**LUI never changes the application's table on its own.** `Ensure()` creates two tables of LUI's and only reads the
application's table — it checks that the table, the named columns and an `INTEGER PRIMARY KEY` are there
(`LuiLinkedTableException` otherwise, nothing is changed). `allowSchemaChange` is gone.

**The layout.** `lui_<name>(doc_id, s_rowid, s_p1, s_p2)` — no text — with the two prefix indexes, and a
**contentless** FTS5 table (`content=''`, `contentless_delete=1`, SQLite ≥ 3.43; LUI needs 3.45) over the search form.
The forms are computed by the same SQL expressions that generate the columns of `Owned`, inside the statements that
write the index. The candidate, prefix and hit statements read text and payload from the application's table by its
primary key. The query path is the one of ADR-0014; `LinkedSource` is the second `ContentSource`.

**The application keeps the index current — explicitly:**

| | |
|---|---|
| `Sync()` | the whole table, in one transaction of LUI's: clears the index, fills it in chunks of 10,000 keys (the cancellation is checked between them), the search index in the order of the rowid, merges it at the end |
| `Refresh(key, transaction = null)`, `RefreshMany(keys, transaction = null)` | after the application's own writes: LUI reads the row, a missing row or one without text leaves the index. Without a transaction LUI opens its own; with the application's, both belong to it |
| `TriggerScript()` | the SQL of three triggers (insert, update of the watched columns, delete) for the application's own migration — nothing is changed by asking. The triggers **only note the key** of a changed row in an outbox, `lui_<name>_pending(doc_id)` |
| `CreateTriggers()`, `DropTriggers()` | create and remove them, only on a call |
| `ApplyPending()`, `Pending()` | bring the noted rows into the index — in batches of 1,000, each in a transaction of LUI's, on its checked connection — and count them. A query never applies them: a read stays a read |
| `Optimize()` | merges the segments; many single refreshes leave deletion marks a query steps over |

`DropIndex` removes LUI's tables, never the application's table or its rows; it decides by the stored source, and refuses
a source it does not know (review of pull request 14, N2). **Triggers of a linked index go only with
`DropIndex(name, dropTriggers: true)`** (review of pull request 16, M3): they write into the tables that go, so they
cannot stay — but a migration of the application may count on them, and their removal is not silent. After the drop
and a new `Ensure()` the index is empty and has no triggers. `Triggers()` tells `None`, `Current` and `Stale` apart — a
migration's triggers after an update of LUI or a change of the table are `Stale` and are recreated. The message of a
`LuiDefinitionMismatchException` of a linked index names `DropIndex(name, dropTriggers: true)` (F2). Rows the triggers
noted stay in the outbox when the triggers are dropped: they changed (F3).

**Values are brought into range, not refused**: a weight outside 0 … 536,870,911 is clamped, a priority outside 0 … 3
is normal. A row without text, and a row whose key is outside 0 … 2³²−1 — it cannot be a document id — is **left out**,
never refused: a refusal in a trigger would roll back the application's own write, and in a sync one such row would
take the whole index with it (review of pull request 16, M2). `Sync` returns a `LuiSyncResult` that counts both. Only
`Refresh` with such a key throws (`ArgumentOutOfRangeException`): that is a mistake of the call, not a write of the
application.

**A text that changed under the index is not a hit** (review of pull request 16, S1). The candidates come from the
index, the text from the table: without a refresh they can disagree. Every candidate of a source whose text can change
under the index is checked against its text as it is now — its folded search form must still hold what the expression
asked for, or begin with the prefix key (`IndexKindRules.StillMatches`). The search form is computed for the word start
anyway; the query p95 at 200,000 rows stays 1.14 ms. A new text is found only after the refresh. **The check comes
after the `LIMIT`** (re-review of pull request 16, F1, pinned by a test): stale entries still use up the budget of
`Candidates`, and with many of them among the best an entry that does match can be missed — 600 entries, the 550
most popular changed without a refresh: no hit with 500 candidates, 20 with 1,000, 20 after `ApplyPending()`. With
triggers a stale index is the normal state between two `ApplyPending()` calls, so they are to run regularly;
`Pending()` is the value for a health check. Reading on past the `LIMIT` when many candidates were dropped belongs to
PR-008, where the outbox gets applied on a timer anyway.

**A table that changed under the index is named** (S4): a missing column or table in a query, a refresh or a sync is a
`LuiLinkedTableException` that says so, not a bare SQLite error. `INTEGER PRIMARY KEY DESC` is refused at `Ensure()`: it
has an index of its own and is no alias of the rowid (S3). A transaction of another database is refused with a clear
message (N5).

**The payload** comes from columns: `WithPayload(["slug", "color"], row => new TagChip(…))`. The `PayloadReader` names
its columns and the hits statement is built from that list (review of pull request 14, S3); `LuiRow` sees only them.

**The builder** (owner's decision): `Owned<long>` is the default — `SingleWord()`/`MultiWord()` return the owned builder,
the source step of ADR-0013 is gone; `WithKey<string>()` chooses a text key. `LinkTo(table, key, text, weight?,
priority?)` is an extension method on `LuiIndexBuilder<long, LuiNoPayload>` only: it is offered as long as no key
and no payload are chosen, and the settings made before it (similarity, transliteration, typo tolerance) carry over.
Names of the application match `^[A-Za-z_][A-Za-z0-9_]{0,63}$` — `Tags` and `UsageCount` as an ORM names them — and
are always quoted.

Also from the review of pull request 14: `CompactColumnDefinition` gives the column, the source the layout (N3);
a key at 2³²−1 comes back through the hits as it is (N6).

## What was measured

The spike (`LinkedLayoutExperiment`, gated) against `Owned` on 200,000 entries of the whole word list: the same
candidates for all 2,000 inputs, fresh and after 2,000 removals and 1,000 weight changes; plan `VIRTUAL TABLE INDEX 64`
plus `SEARCH h USING INTEGER PRIMARY KEY`, the prefix path on a covering index; integrity-check and optimize pass. The
index is **36 MB against 45 MB**. Query p95 before a merge 1.45 vs 1.15 ms, after the writes 1.78 vs 1.05 ms (the
deletion marks), after optimize 0.82–0.84 vs 0.92 ms.

The implementation (`LinkedPerformance`, gated, two runs at 200,000, one at 1,000,000):

| | 200,000 rows | 1,000,000 rows |
|---|---:|---:|
| `Sync` of the table | 3.5–3.9 s | 26.7 s |
| `UpsertMany` of the same entries into `Owned` | 8.2 s | 50.8 s |
| query p50 / p95 through `Search`, linked | 0.52 / 1.11–1.17 ms | 1.10 / 3.43 ms |
| query p50 / p95, owned | 0.59–0.61 / 1.25–1.33 ms | 1.41 / 4.72 ms |
| `Refresh` of one row, own transaction, p50 / p95 | 0.31–0.33 / 0.51–0.58 ms | 0.94 / 1.67 ms |

The comparison is not like for like, and it is not claimed as one: `Sync` writes the search index sorted and merges it,
`UpsertMany` builds it row by row through the triggers and does not merge. It shows what the bulk path of ADR-0010
gives — the bulk path for `Owned` stays in PR-007.

## Consequences

- **The triggers are an outbox** (review of pull request 16, M1; owner's decision). The first version wrote the
  contentless FTS5 table in the trigger; the review showed with SQLite 3.40.1 that every program writing the table then
  needs SQLite ≥ 3.43 with FTS5 — an older one failed the application's own write — and that the normalisation ran on
  the writer's SQLite, never checked by `FoldProbe`. Now a trigger is one `INSERT OR IGNORE` of a key into a plain table:
  any SQLite writes the table, a key out of range is noted and later left out, and the forms are computed only by LUI,
  on its checked connection, in `ApplyPending()`. The price is the consistency: with triggers the index is as current as
  the last `ApplyPending()` — not as the write. A hosted service that applies them on a timer comes with the dependency
  injection (PR-008). Verified: the trigger script holds no FTS5 and no normalisation (a test); a second SQLite (Python's
  3.45.1, not the one LUI bundles) writes, changes and deletes through the triggers. A SQLite below 3.43 could not be
  built in this environment (sqlite.org is not reachable); the reviewer of pull request 16 **measured** it with SQLite
  3.40.1 — the application writes, changes and deletes through the triggers (follow-up review; noted in ADR-0016).
- **A sync is one write transaction** (S2): other writers of the file wait up to their busy timeout — a million rows take
  about half a minute, so a writer with a shorter timeout gets `SQLITE_BUSY`. A shadow build in chunks with a short swap
  belongs to PR-007, measured first.
- An application decides how current its index is. Without triggers and without refreshes, a linked index is as
  current as its last `Sync` — the same promise `Owned` makes for writes the index is not told of.
- The typo dictionary learns the words of `Refresh` and `Sync`; a write it is not told of reaches it with
  `RebuildTypoDictionary()`. A refresh in a transaction that rolls back leaves its words in the dictionary, as an
  upsert does (ADR-0013).
- A table without an `INTEGER PRIMARY KEY` (a text key, `WITHOUT ROWID`) cannot be linked; it can be indexed `Owned`
  with `WithKey<string>()`.
- `Check()` for `Linked` comes with PR-007, as for `Owned`.

**Verified by:** `tests/Nordmann.LUI.Integration.Tests/LinkedIndexTests.cs` (43 tests; from the review of pull request 16: the outbox — noted, applied, out of the outbox after a refresh and a sync, more than one batch, a script without FTS5 —, a key out of
range in a sync and through a trigger, a drop refused with triggers and done with `dropTriggers`, the trigger states, a
stale text in a single- and a multi-word index, a renamed column, a key twice in a refresh, a transaction of another
database, a payload column named twice, `INTEGER PRIMARY KEY DESC`; and before: the schema of the application's table
before and after; refused tables and names; sync; refresh with and without a transaction, rollback, cancellation;
weights and priorities in range; payload from columns; the key at 2³²−1; multi-word; the same answers as `Owned`; the
typo dictionary; the trigger script, created and dropped triggers; drop; definition mismatch; a source this version does
not know; quoted ORM names) — two of them checked with a deliberate fault (a refresh that does not remove, a sync that
does not clear: two failures each) —, `SchemaGoldenTests` (the schema of `Owned`, byte for byte), and the package smoke
test, which runs the linked block of the README against the packed package.
