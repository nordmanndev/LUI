# 0015 — The linked index is loosely coupled: LUI never changes the application's table on its own

- **Status:** Accepted
- **Date:** 2026-11-03
- **Supersedes:** the `Linked` design of the specification, draft 2 (§5, §7.3, §12.2: generated columns, indexes and
  triggers on the application's table behind `allowSchemaChange`); the source step of the builder in ADR-0013
- **Affects:** `src/Nordmann.LUI/Schema/LinkedSource.cs`, `Schema/ContentSources.cs`, `Schema/LinkDefinition.cs`,
  `Indexes/LinkedIndex.cs`, `Indexes/LinkedTable.cs`, `Querying/ColumnPayload.cs`, `LuiLinkedIndexBuilder.cs`,
  `LuiIndexBuilderExtensions.cs`, `ILuiLinkedIndex.cs`, `LuiRow.cs`, `LuiLinkedTableException.cs`, `LuiIndexBuilder.cs`,
  `LuiOwnedIndexBuilder.cs`; `docs/spec/LUI-Spezifikation.md` §5, §7.3, §12, §14, §20; `AGENTS.md`; `README.md`

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
| `TriggerScript()` | the SQL of three triggers (insert, update of the watched columns, delete) for the application's own migration — nothing is changed by asking |
| `CreateTriggers()`, `DropTriggers()` | create and remove them, only on a call |
| `Optimize()` | merges the segments; many single refreshes leave deletion marks a query steps over |

`DropIndex` removes LUI's tables and the triggers it created, never the application's table or its rows; it decides
by the stored source, and refuses a source it does not know (review of pull request 14, N2).

**Values are brought into range, not refused**: a weight outside 0 … 536,870,911 is clamped, a priority outside 0 … 3
is normal, a row without text is not indexed. A key outside 0 … 2³²−1 is refused — by `Refresh` with an
`ArgumentOutOfRangeException`, by `Sync` with a `LuiLinkedTableException` that changes nothing: the key is the document
id.

**The payload** comes from columns: `WithPayload(["slug", "color"], row => new TagChip(…))`. The `PayloadReader` names
its columns and the hits statement is built from that list (review of pull request 14, S3); `LuiRow` sees only them.

**The builder** (owner's decision): `Owned<long>` is the default — `SingleWord()`/`MultiWord()` return the owned builder,
the source step of ADR-0013 is gone; `WithKey<string>()` chooses a text key. `LinkTo(table, key, text, weight?,
priority?)` is an extension method on `LuiOwnedIndexBuilder<long, LuiNoPayload>` only: it is offered as long as no key
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

- An application decides how current its index is. Without triggers and without refreshes, a linked index is as
  current as its last `Sync` — the same promise `Owned` makes for writes the index is not told of.
- The typo dictionary learns the words of `Refresh` and `Sync`; a write it is not told of reaches it with
  `RebuildTypoDictionary()`. A refresh in a transaction that rolls back leaves its words in the dictionary, as an
  upsert does (ADR-0013).
- A table without an `INTEGER PRIMARY KEY` (a text key, `WITHOUT ROWID`) cannot be linked; it can be indexed `Owned`
  with `WithKey<string>()`.
- `Check()` for `Linked` comes with PR-007, as for `Owned`.

**Verified by:** `tests/Nordmann.LUI.Tests/LinkedIndexTests.cs` (30 tests: the schema of the application's table
before and after; refused tables and names; sync; refresh with and without a transaction, rollback, cancellation;
weights and priorities in range; payload from columns; the key at 2³²−1; multi-word; the same answers as `Owned`; the
typo dictionary; the trigger script, created and dropped triggers; drop; definition mismatch; a source this version does
not know; quoted ORM names) — two of them checked with a deliberate fault (a refresh that does not remove, a sync that
does not clear: two failures each) —, `SchemaGoldenTests` (the schema of `Owned`, byte for byte), and the package smoke
test, which runs the linked block of the README against the packed package.
