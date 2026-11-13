# Active task

<!--
What the next session is doing. One task, in the words of the issue that
describes it. Link the issue; do not repeat the whole plan here.
-->

**Issue:** [#144](https://github.com/nordmanndev/LUI/issues/144) — the public surface of 0.1.0 is written down against
the review of 2026-11-04. The paper before the release, and the closing of epic #39.
**Branch:** `docs/144-the-surface-of-0-1-0`, from `dev` at `d0435b0`.
**Decision:** [ADR-0063](../../.agents/adr/0063-the-surface-of-0-1-0-is-the-review-of-2026-11-04-applied.md) — every point
of the review of 2026-11-04, and the type or the member that carries it today.

## What is done

- **The series is complete**: steps 1 to 8 of epic #117 are merged into `dev` (PRs #126, #127, #131, #135, #136, #138,
  #139, #141), and the residue of the German is gone (PR #143, `9d5ca7a`).
- **The route is named in the call** (PR #146, `d0435b0`): `LuiCatalog.Create` creates an embedded index,
  `LuiCatalog.CreateLinked` a linked one. The owner's decision of 2026-11-13 on point A.3 of the review.
- **The review of 2026-11-04 is checked against the code, point by point** — the ones that were decided and the ones
  that were declined. Four points were taken up again afterwards: three of them decided differently (the entry point
  ADR-0033, the types ADR-0035, the typed view ADR-0044), one of them the way the review wanted it (A.3, ADR-0062).
- **A row of the freeze had become untrue**: ADR-0026 said of A.2 that the name `LuiServicesBuilder` stays. The type is
  gone with the entry point; the row now carries a dated note that says so.

## Open

- **The pull request of #144** is open against `dev`. The acceptance criteria of the issue: every point of the review has
  a line, and every line names the member that carries it today.
- **The `dev` → `main` release pull request** comes after that, and it carries its own review (`AGENTS.md`).

## The next step

The **release pull request** — the owner asks for it (`AGENTS.md`). After that the backlog of the open issues: the
documentation of the tests (#81), the three guards that the work found (#128, #129, #130), the two open bugs of the
measurement (#134, #137), and the three that wait for after 0.1.0 (#31, #32, #44).
