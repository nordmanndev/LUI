# Active task

<!--
What the next session is doing. One task, in the words of the issue that
describes it. Link the issue; do not repeat the whole plan here.
-->

**Issue:** [#22](https://github.com/nordmanndev/LUI/issues/22) — The test tree: three layers, one map
**Branch:** `feat/test-landscape`
**State:** implemented, measured, documented (ADR-0017); the pull request is open.

## What this one did

The one test project became three (`Nordmann.LUI.Unit.Tests`, `Nordmann.LUI.Integration.Tests`,
`Nordmann.LUI.Architecture.Tests`) plus the shared `Nordmann.LUI.Testing`. `docs/testing.md` says which test holds
which rule up, and `Test_Landscape` fails when the map and the tree disagree. The pin of `Alba` is gone; the guard
now checks both directions of the package rule.

## Open for the owner

- The three `Microsoft.Extensions.*` pins stay on purpose (ADR-0013, PR-008) and are the only exception of the
  "every pin is used" rule.

## The next step

**PR-008 — DI and query polish:** `AddLui`, keyed services, ensure on start; a hosted service for `ApplyPending`;
reading on past the LIMIT when stale candidates use up the budget (F1 of the review of PR #16); `Highlight`.
