# Active task

<!--
What the next session is doing. One task, in the words of the issue that
describes it. Link the issue; do not repeat the whole plan here.
-->

**Issue:** [#11](https://github.com/nordmanndev/LUI/issues/11) — API shape before 0.1.0 (DevEx review)
**Branch:** `feat/api-shape`
**State:** implemented, measured and verified; the pull request is opened when the owner asks.

## Where the work stands

- The public API has its pre-release shape (ADR-0013). Open decision of the owner: ADR-0010 (the §16 query budget at
  3 M entries).

## The next step

**PR-005b — the seams for `Linked`** (refactoring, no behaviour change): the index kind as one strategy (`IndexKindRules`:
indexed column, prefix source, norm storage, default similarity, prefix path, expression) instead of `kind ==` in four files;
the content source as a strategy (`IContentSource`: DDL, candidate/prefix/hit SQL, payload reader, typo source) designed
against the `Linked` list below; the namespace direction fixed by NetArchTest; `QueryRunner` split into stages. The golden,
plan and performance numbers must stay the same.

**Then PR-006 — `Linked`:**

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
