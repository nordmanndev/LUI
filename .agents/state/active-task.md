# Active task

<!--
What the next session is doing. One task, in the words of the issue that
describes it. Link the issue; do not repeat the whole plan here.
-->

**Issue:** [#15](https://github.com/nordmanndev/LUI/issues/15) — Linked index, loosely coupled (PR-006)
**Branch:** `feat/linked-index`
**State:** implemented, measured and verified; the pull request is opened when the owner asks.

## Where the work stands

- **The owner's decisions (2026-11-03):** no column, no index, no implicit trigger on the application's table — `Ensure()`
  creates `lui_*` tables only; the application keeps the index current with `Sync`, `Refresh(id, transaction = null)`,
  `RefreshMany(ids, transaction = null)` (the transaction is always optional: without one LUI opens its own, reads the row
  and writes the index in it); triggers only on request: `TriggerScript()`, `CreateTriggers()`, `DropTriggers()`.
  `allowSchemaChange` goes away. Builder: `Owned<long>` default, `WithKey<string>()`, `LinkTo(…)` only before a key or a
  payload.
- **The spike** (`LinkedLayoutExperiment`, gated, 200,000 entries, the whole word list): a contentless FTS5 table
  (`content=''`, `contentless_delete=1`) + `lui_<name>(doc_id, s_rowid, s_p1, s_p2)` without text; text from the
  application's table by primary key. Same candidates as `Owned` for all 2,000 inputs, fresh and after 2,000 removals and
  1,000 weight changes; plan `VIRTUAL TABLE INDEX 64` + `INTEGER PRIMARY KEY`, the prefix path on a covering index;
  integrity-check and optimize pass. File: index 36 MB against 45 MB (−20 %). Query p95: fresh 1.45 vs 1.15 ms, after the
  writes 1.78 vs 1.05 ms (the tombstones of contentless_delete), **after optimize 0.82–0.84 vs 0.92 ms**.
- Open decisions of the owner: ADR-0010; commit hooks for the typo dictionary.

## The next step

**PR-006 — `Linked`** — with the seams of ADR-0014: a `LinkedSource : ContentSource` (the host table's names, the five
generated columns, three indexes, three triggers; `TextsSql` from the host columns), a column `PayloadReader`
(`WithPayload(columns, row => …)`), `LinkedIndex : ILuiIndex` in `Indexes`, `LuiIndexSourceBuilder.LinkedTo(…)` and
`ContentSource.DropStatements` by the stored source.

**Owner's decision (2026-11-03), part of PR-006:** `Owned<long>` is the default — `SingleWord()`/`MultiWord()` return the
owned builder with a `long` key, the source step goes away; `WithKey<TKey>()` chooses `string`. `LinkTo(table, key, text,
weight, priority, allowSchemaChange)` (the name replaces the spec's `LinkedTo`) switches to the linked builder and is
offered only on `LuiOwnedIndexBuilder<long, LuiNoPayload>` (an extension method: the compiler refuses it after `WithKey` or
`WithPayload`). Spec §12.2, ADR-0013 and the README quick start follow; an ADR records the change.

**From the review of PR #14, for PR-006:** S3 — the hits SQL and its `PayloadReader` come from the source together (the
columns of the payload and the order they are read in must not be decided in two places); N2 — `ContentSource.DropStatements`
decides by the stored source (a test that fails for an unknown one); N3 — `CompactColumn` returns the column definition only,
the source adds comma and layout (`ALTER TABLE … ADD COLUMN` for `Linked`); N6 — a `long` key near 2³²−1 read back through
`HitReader` (the key is read as text today). For PR-007 (D4): remove the named `LuiCatalog` exception of
`Internal_Layering.No_layer_goes_up_through_the_facade`.

**Measuring against `main`:** a worktree has no `artifacts/` — copy `artifacts/wordlist/german-words.txt` into it, or
`QueryPerformance` silently measures the sample list (review of PR #14, N5).

PR-006 — `Linked`: an index over a table of the application (spec §5, §7.3, §12.2, §12.8). `LinkedTo(table, key, text, weight,
priority, allowSchemaChange)`; five generated columns, three indexes and three triggers on the application's table; the payload from
columns of the table; `LuiSchemaChangeRequiredException`.

**Read first:** `AGENTS.md` (the Ask-before line: the schema of a `Linked` host table), `docs/spec/LUI-Spezifikation.md` §5, §7.3,
§12.2, §12.3, §12.8, `docs/adr/0006`, `0011`.

**The traps:**

- The library changes the application's schema **only** in `Ensure()` and only with `allowSchemaChange: true`; otherwise it throws with
  the SQL it would run.
- `TKey` is `long` (the primary key = `doc_id`, 0 … 4,294,967,295). The weight column is `INTEGER NOT NULL` in 0 … 2^29−1; the priority
  column 0 … 3 or absent.
- There is no `Upsert` on a linked index — the application writes to its table, the triggers do the rest. `UpdateWeight` is an `UPDATE`.
- The typo dictionary reads `ContentSource.TextsSql` (`text`, `weight`); for `Linked` the columns have other names and the new words arrive without `Upsert`.
  Decide how the dictionary learns them (rebuild on demand is the minimum).
- Solved by ADR-0014: the content table and its columns come from `ContentSource`; `Linked` is a second subclass, not a parameter.
