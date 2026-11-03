# Active task

<!--
What the next session is doing. One task, in the words of the issue that
describes it. Link the issue; do not repeat the whole plan here.
-->

**Issue:** [#17](https://github.com/nordmanndev/LUI/issues/17) — Sync, Check, Rebuild, Optimize (PR-007)
**Branch:** `feat/sync-check-rebuild`
**State:** implemented, measured, documented (ADR-0016); waiting for the owner's go to open the pull request.

## Open for the owner

- The default `LuiSyncOptions.BatchSize`: 10,000 (spec, lock < 1 s) vs 50,000 / 100,000 (20–26 % faster, lock 2–5 s).
- A shadow build for `Linked.Sync()` (review of PR #16, S2): one transaction of 26.7 s per million rows today.
- ADR-0010 (the query budget at 3 M) — measure again after this bulk path, with PR-009.

## The next step

**PR-008 — DI and query polish:** `AddLui`, keyed services, ensure on start; a hosted service for `ApplyPending`;
reading on past the LIMIT when stale candidates use up the budget (F1 of the review of PR #16); `Highlight`.
