# Active task

<!--
What the next session is doing. One task, in the words of the issue that
describes it. Link the issue; do not repeat the whole plan here.
-->

**Epic:** [#39](https://github.com/nordmanndev/LUI/issues/39) — the public API before the freeze of 0.1.0 (architecture review of 2026-11-04)
**Issue:** [#48](https://github.com/nordmanndev/LUI/issues/48) — the typo dictionary only grows (option G, ADR-0025)
**Branch:** `refactor/typo-dictionary-only-grows`
**State:** implemented and verified; pushed; the pull request waits for the owner's word.

## Decided by the owner (2026-11-04)

- Epic #39 as recommended: #40 (merged, #45) → #41 (merged, #46) → #42 (this, one pull request) → #43 (the freeze).
  Not done: A.2, A.4 (`LuiPriority` stays), the merged sync type, `With…` methods, keyed registrations, D.3.
- There are no old databases (LUI is in development): no legacy paths, no "way out" messages.
- #35 after 0.1.0; E.1 with #30; #44 (`symspell`) after 0.1.0. The first release stays 0.1.0.
- #48: option G — the dictionary only grows, no `Stale`, no background rebuild, one dictionary per index ("jeder Index hat sein eigenes Dictionary"). ADR-0025. The freeze (#43) becomes ADR-0026.

## The next step

**Next:** #43 — ADR-0026, the freeze: the public API from 0.1.0 on is additive only, and it lists the rejected points of the
review by name (review of #45, M4); then PR-011 (sample) and PR-012 (release; ask before publishing).
