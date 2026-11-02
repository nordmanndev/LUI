# Active task

<!--
What the next session is doing. One task, in the words of the issue that
describes it. Link the issue; do not repeat the whole plan here.
-->

**Issue:** [#5](https://github.com/nordmanndev/LUI/issues/5) — SingleWord + Owned index
**Branch:** `feat/single-word-index`
**State:** PR-003 is implemented and measured; waiting for review.

## Where the work stands

- `LuiCatalog`, the builder, `ILuiIndex`, the write API, the query path and
  the ranking exist for `SingleWord` + `Owned` (ADR-0006 … ADR-0009).
- The tests are green: unit, SQLite integration, golden queries, the plan
  test, concurrency. The measurement is in `docs/benchmarks.md`.

## The next step

PR-004 — `MultiWord`: the word split, `s_compact`, the word-start rule on the
compact form, N-gram Dice (spec §4, §8.2, §9.1).

**Read first:** `AGENTS.md`, `docs/spec/LUI-Spezifikation.md` §4, §8.2, §9,
`docs/adr/0006` and `0008`.

**The traps:**

- The word-start rule already works on the compact form (`WordStart`), and
  `Ranker` already knows the groups; `MultiWord` needs the FTS5 expression
  `(s_norm : ("apfel" AND "kuch")) OR (s_compact : "apfelkuch")` and the
  second FTS column. The definition in `lui_meta` has to change with it
  (`IndexDefinition.Kind` already does).
- A word of fewer than three characters does not go into the expression, but
  into the ranking (§8.2).
- The prefix path of a `MultiWord` index finds the beginning of the *text*,
  not of every word (§21 O1). Do not widen it here.
