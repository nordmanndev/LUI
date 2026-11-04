# Active task

<!--
What the next session is doing. One task, in the words of the issue that
describes it. Link the issue; do not repeat the whole plan here.
-->

**Epic:** [#39](https://github.com/nordmanndev/LUI/issues/39) — the public API before the freeze of 0.1.0 (architecture review of 2026-11-04)
**Issue:** [#41](https://github.com/nordmanndev/LUI/issues/41) — `Owned` → `Embedded`
**Branch:** `refactor/owned-to-embedded`
**State:** implemented, verified, pushed; the pull request waits for the owner's word.

## Decided by the owner (2026-11-04)

- Epic #39 as recommended: #40 (merged, #45) → #41 (this) → #42 (API polish) → #43 (the freeze).
  Not done: A.2, A.4 (`LuiPriority` stays), the merged sync type, `With…` methods, keyed registrations, D.3.
- #35 after 0.1.0; E.1 with #30; #44 (`symspell`) after 0.1.0. The first release stays 0.1.0.
- ADR numbers follow the order of writing: 0023 is the rename (#41); #42 gets 0024 and the freeze (#43) 0025.

## The next step

**Next:** #42 — the API polish, then #43 (the freeze ADR, listing the rejected points by name — review of #45, M4);
then PR-011 (sample) and PR-012 (release; ask before publishing).
