# Active task

<!--
What the next session is doing. One task, in the words of the issue that
describes it. Link the issue; do not repeat the whole plan here.
-->

**Issue:** [#148](https://github.com/nordmanndev/LUI/issues/148) — the host stops the warm-up, and a round that runs
goes on. Found while the documentation of the tests was written (#81).
**Branch:** `fix/148-the-stop-waits-for-the-round`, from `dev` at `87578de`.
**Decision:** none — ADR-0043 already decides it ("the host runs it, the host stops it, the host disposes it"); the
record gets a dated note.

## What is done

- **The series is complete**: steps 1 to 8 of epic #117 are merged into `dev` (PRs #126, #127, #131, #135, #136, #138,
  #139, #141), and the residue of the German is gone (PR #143, `9d5ca7a`).
- **The route is named in the call** (PR #146, `d0435b0`): `LuiCatalog.Create` creates an embedded index,
  `LuiCatalog.CreateLinked` a linked one.
- **The surface of 0.1.0 is written down against the review of 2026-11-04** (PR #147, `87578de`): every point of the
  review, and the type or the member that carries it today.
- **The warm-up leaves nothing running**: the stop and the disposal wait for a round that runs, and a round that begins
  after the stop does not begin. The teardown of a test waits for the file to be free. The integration lane is green in
  four runs side by side — it failed in about one of three before.

## Open

- **The pull request of #148** is open against `dev`.
- **The pull request of #81** — the chapter that says why the tests are built the way they are — is open against `dev`
  as well.
- **The `dev` → `main` release pull request** comes after that, and it carries its own review (`AGENTS.md`).

## The next step

The **release pull request** — the owner asks for it (`AGENTS.md`). After that the backlog of the open issues: the three
guards that the work found (#128, #129, #130), the two open bugs of the measurement (#134, #137), and the three that wait
for after 0.1.0 (#31, #32, #44). The epics #39, #64 and #72 are closed with the last of the steps.
