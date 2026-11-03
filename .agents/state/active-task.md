# Active task

<!--
What the next session is doing. One task, in the words of the issue that
describes it. Link the issue; do not repeat the whole plan here.
-->

**Issue:** [#13](https://github.com/nordmanndev/LUI/issues/13) — Seams for Linked (PR-005b)
**Branch:** `refactor/linked-seams`
**State:** implemented, measured and verified; the pull request is opened when the owner asks.

## Where the work stands

- ADR-0014: `Schema/IndexKindRules` (+ `SingleWordRules`, `MultiWordRules`), `Schema/ContentSource` (+ `OwnedSource`),
  `Querying` as a pipeline (`CandidateReader`, `TypoCorrection`, `HitReader`, `PayloadReader`), namespaces `Indexes` /
  `Querying` / `Writing` / `Schema` / `Typos` / `Normalization` with one direction (NetArchTest).
- Open decisions of the owner: ADR-0010 (the §16 query budget at 3 M entries); whether the typo dictionary should learn
  only committed writes (commit hooks, review of PR #12).

## The next step

**PR-006 — `Linked`** — with the seams of ADR-0014: a `LinkedSource : ContentSource` (the host table's names, the five
generated columns, three indexes, three triggers; `TextsSql` from the host columns), a column `PayloadReader`
(`WithPayload(columns, row => …)`), `LinkedIndex : ILuiIndex` in `Indexes`, `LuiIndexSourceBuilder.LinkedTo(…)` and
`ContentSource.DropStatements` by the stored source.

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
