# Active task

<!--
What the next session is doing. One task, in the words of the issue that
describes it. Link the issue; do not repeat the whole plan here.
-->

**Epic:** [#39](https://github.com/nordmanndev/LUI/issues/39) — the public API before the freeze of 0.1.0 (architecture review of 2026-11-04)
**Issue:** [#40](https://github.com/nordmanndev/LUI/issues/40) — internal: one settings record, two registration types, one periodic loop
**Branch:** `refactor/internal-settings-registrations`
**State:** implemented, verified, pushed; the pull request waits for the owner's word.

## Decided by the owner (2026-11-04)

- Epic #39 as recommended: #40 (internal) → #41 (`Owned` → `Embedded`) → #42 (API polish) → #43 (ADR-0023, the freeze).
  Not done: A.2, A.4 (`LuiPriority` stays), the merged sync type, `With…` methods, keyed registrations, D.3.
- #35 after 0.1.0; E.1 with #30; #44 (`symspell`) after 0.1.0. The first release stays 0.1.0.

## The next step

**Next:** #41 — `Owned` → `Embedded`, then #42, #43; then PR-011 (sample) and PR-012 (release; ask before publishing).
