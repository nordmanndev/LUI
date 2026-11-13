# Active task

<!--
What the next session is doing. One task, in the words of the issue that
describes it. Link the issue; do not repeat the whole plan here.
-->

**Issue:** [#81](https://github.com/nordmanndev/LUI/issues/81) — the documentation of the tests: how they are built, and
why. The last step of epic #72.
**Branch:** `docs/81-the-testing-strategy`, from `dev` at `715c121`.
**Decision:** none — the chapter is a document, not a decision. The rules it explains are the ones of ADR-0019, ADR-0049,
ADR-0050, ADR-0051, ADR-0056 and ADR-0058.

## What is done

- **The series is complete**: steps 1 to 8 of epic #117 are merged into `dev` (PRs #126, #127, #131, #135, #136, #138,
  #139, #141), and the residue of the German is gone (PR #143, `9d5ca7a`).
- **The route is named in the call** (PR #146, `d0435b0`): `LuiCatalog.Create` creates an embedded index,
  `LuiCatalog.CreateLinked` a linked one.
- **The surface of 0.1.0 is written down against the review of 2026-11-04** (PR #147, `87578de`): every point of the
  review, and the type or the member that carries it today.
- **The warm-up leaves nothing running** (PR #149, `715c121`): the stop and the disposal wait for a round that runs, and
  the integration lane is green in four runs side by side — it failed in about one of three before.
- **The chapter exists**: `docs/testing-strategy.md` — the layers, the techniques and their reason, what is deliberately
  not done, and what a new test brings with it. The drift of the prose is gone: the durations of the map are orders of
  magnitude, and the soak of the concurrency tests says six readers over four thousand entries, as the code does.

## Open

- **The pull request of #81** is open against `dev`. The acceptance of the issue: the link check and the map are green,
  and **the owner has read the chapter**.
- **The `dev` → `main` release pull request** comes after that, and it carries its own review (`AGENTS.md`).

## The next step

The **release pull request** — the owner asks for it (`AGENTS.md`). After that the backlog of the open issues: the three
guards that the work found (#128, #129, #130), the two open bugs of the measurement (#134, #137), and the three that wait
for after 0.1.0 (#31, #32, #44). The epics #39, #64 and #72 are closed with the last of the steps.
