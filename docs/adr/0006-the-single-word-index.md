# 0006 — The single-word index: one rowid, three indexes, one external-content FTS5 table

> **Name note (ADR-0023, 2026-11-04):** since this record was written, `Owned` is called `Embedded`: `ILuiOwnedIndex` is
> `ILuiEmbeddedIndex`, `LuiIndexBuilder` is `LuiEmbeddedIndexBuilder`, and `OwnedIndex`, `OwnedSource`, `OwnedSync` and
> `OwnedCheck` are `Embedded…`. The text below is history and stays as it was written.

- **Status:** Accepted
- **Date:** 2026-11-02
- **Supersedes:** —
- **Affects:** `src/Nordmann.LUI/Indexing/` (`OwnedSchema`, `RowId`, `OwnedIndex`),
  `src/Nordmann.LUI/LuiIndexBuilder.cs`, `src/Nordmann.LUI/ILuiIndex.cs`

## Context

Step 3 of the plan is the first index that answers a query: `SingleWord` over
an `Owned` content table. The specification fixes the mechanism
(§3 E1–E3, E10; §7.2, §7.4) and the measurements in Anhang A back it. What it
leaves to the implementation is the places where the mechanism meets a real
SQLite and a real API.

## Decision

**The schema is the specification's, for a single-word index.** One content
table `lui_<name>` with the generated columns `s_norm`, `s_p1`, `s_p2` and
`s_rowid`; the unique index on `s_rowid` and the two prefix indexes; one
external-content FTS5 table `lui_<name>_fts` with `tokenize = 'trigram
remove_diacritics 1'`; the three triggers. There is no `s_compact` (§7.2) —
it arrives with `MultiWord`.

**Priority and weight live in the rowid, and the query reads them from it.**
`s_rowid = (priority << 61) | ((2²⁹−1 − weight) << 32) | doc_id`. The
candidate statements select `s_rowid` and the text, nothing else: priority,
weight and `doc_id` of a candidate are decoded from the rowid in C#
(`RowId`). The round trip and the order are unit tests, and a test compares
the C# encoder with the SQL expression row by row.

**Five places where the implementation differs from the specification:**

| | Specification | Here | Why |
|---|---|---|---|
| `doc_id` | `INTEGER PRIMARY KEY` | `… CHECK (doc_id BETWEEN 0 AND 4294967295)` | `doc_id` is the lowest 32 bits of the rowid. The 4,294,967,296th entry would silently overwrite the weight bits; with the check it fails loudly. |
| the key | `TEXT` | `TEXT`, for `long` and for `string` alike | One column, one comparison; a `long` is written in the invariant culture and read back with `long.Parse`. |
| index names | `^[a-z][a-z0-9_]{0,40}$` | the same, and **not** `meta`, **not** containing `_fts` | `lui_meta` is the catalog, and `lui_<name>_fts` plus its shadow tables must not be able to collide with the content table of another index. |
| the join from the search index to the content table | `c.s_rowid = f.rowid` | `c.doc_id = (f.rowid & 4294967295)` | The text of a candidate is fetched by the primary key, in the table itself: one seek, where the join on `s_rowid` seeks in an index first and then in the table. A **covering** index `(s_rowid, text)` would save the same hop, but SQLite does not treat an index that contains a generated column as covering (measured: `USING INDEX`, never `USING COVERING INDEX`). |
| the update trigger | `AFTER UPDATE OF text, weight, priority` | the same, with `WHEN old.s_rowid IS NOT new.s_rowid OR old.s_norm IS NOT new.s_norm` | `ON CONFLICT … DO UPDATE SET text = excluded.text` names `text`, so the trigger fired for an upsert that changed nothing — a search-index delete and insert, and a new small segment (reproduced in review: 50 identical upserts grew `fts_data` from 3 to 8 rows). Now an identical upsert and a change of the payload alone leave the search index untouched. |

**The write API is the one of §12.4**, synchronous, with the application's
`SqliteTransaction` as an optional last parameter. `UpdateWeight`,
`UpdatePriority` and `Remove` return whether the entry existed. **`Upsert`
replaces the whole entry**, the payload included: an upsert without a payload
clears the stored one (documented on `ILuiIndex.Upsert`; a null payload — for a
value type its default — is "no payload", stored as `NULL`). `Upsert`
refuses an empty text and a payload for an index that has no serialiser — a
payload that is silently dropped is a bug the caller finds a month later.

**`Ensure()` is one `BEGIN IMMEDIATE` transaction**: read `lui_meta`, create
or compare, write. Two processes that ensure the same index cannot both
create it. `Drop()` lives on the builder, because the way out of a
`LuiDefinitionMismatchException` must not need a valid index object.

## Consequences

- **Known limitation: the prefix path folds ASCII only.** `s_p1` and `s_p2`
  are built with SQLite's `lower()` (§6.2), which knows ASCII. German needs
  nothing more — the transliteration has already turned `Ä` into `ae`. But a
  text that begins with another accented capital (`Éclair`, `Çay`) is found by
  `ecl` (the trigram path folds with the tokenizer) and **not** by `e`. A test
  pins this so that a change to it is a decision. The way out, if a corpus
  needs it, is a longer replace chain in the two columns — measured before it
  is taken, because it is paid on every write.
- `UpdateWeight` and `UpdatePriority` are a delete and an insert in the
  search index (§18). A heavy re-ranking job pays for that; a click counter
  that updates once an hour does not.
- **The ids are not reused by design, but they are not remembered either.** A
  new entry gets `max(doc_id) + 1`, so removing the highest entry frees its id
  for the next one. The query reads candidates and hits in one snapshot for
  exactly that reason (ADR-0008). When the ids run out the write fails with an
  `InvalidOperationException` that says what to do, not with a bare `CHECK`.
- The rowid caps the stock at 2³² − 1 entries per index and the weight at
  2²⁹ − 1 (§18). Both are checked before they reach SQL.
