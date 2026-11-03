# Active task

<!--
What the next session is doing. One task, in the words of the issue that
describes it. Link the issue; do not repeat the whole plan here.
-->

**Issue:** [#9](https://github.com/nordmanndev/LUI/issues/9) — Typo tolerance
**Branch:** `feat/typo-tolerance`
**State:** PR-005 is implemented and measured; the pull request is open.

## Where the work stands

- `Typos/TypoDictionary` (SymSpell over the word beginnings, lazy, a lock), the correction in `QueryRunner`
  (variants, union, per-candidate input), `Builder.TypoTolerance`, `LuiQuery.TypoTolerance`, `CorrectedQuery`.
  ADR-0012. The specification (§10.3a) was extended with it.
- Open decision of the owner: ADR-0010 (the §16 query budget at 3 M entries).

## The next step

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
- The typo dictionary reads `text` and `weight`; for `Linked` the columns have other names and the new words arrive without `Upsert`.
  Decide how the dictionary learns them (rebuild on demand is the minimum).
- `OwnedSchema`/`OwnedSql`/`OwnedIndex` hard-code the content table `lui_<name>` and its columns; `Linked` needs them parameterised.
