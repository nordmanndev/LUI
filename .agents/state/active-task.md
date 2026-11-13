# Active task

<!--
What the next session is doing. One task, in the words of the issue that
describes it. Link the issue; do not repeat the whole plan here.
-->

**Issue:** none — the first release is out, and the next task is the owner's pick out of the backlog.
**Branch:** `dev` is at `aaf282d`, `main` at `e316c4b` — the tag `v0.1.0` is on the commit of `main`.
**Decision:** none — the release itself was decided (ADR-0048, ADR-0053, ADR-0026).

## What is done

- **LUI 0.1.0 is released** (2026-11-13): the tag `v0.1.0` on `main`, the packages `Nordmann.LUI` and
  `Nordmann.LUI.Hosting` at the version `0.1.0`, and the freeze of
  [ADR-0026](../adr/0026-the-public-api-from-0-1-0-on-grows-only-additively.md) in force — the public API grows only
  additively from this version on.
- **The whole series is merged**: the public API of the review of 2026-11-04 (ADR-0063), the route named in the call
  (ADR-0062), the chapter that says why the tests are built this way (#81), the warm-up that leaves nothing running
  (#148), and the repository English throughout (ADR-0057).
- **The epics #39, #64 and #72 are closed** with the release.

## Open

The backlog, in the order the work found it:

- **#130** — the API page guards the types, not the members.
- **#129** — the index of the ADRs gets a guard.
- **#128** — the C# blocks of the tutorials are compiled by a guard.
- **#137** — dead paths in the records: the files of the measurements moved, the records still name the old places.
- **#134** — the origin of the 1912 bytes inside the measured window is open.
- **#117** — the epic of the public surface and the measurement of its performance: the remaining step is #137 and
  #134, and then the measurement lane of the week says whether the budgets still hold.
- **#44, #32, #31** — the three that wait for after 0.1.0: the dependency on `symspell`, the first load of three million
  entries, the shadow build of `Linked.Sync()`.

## The next step

The owner's pick out of the three guards (#130, #129, #128) — they are small, they are mechanical, and each of them
closes a gap a reader of the repository can fall into. After them: #137 and #134, the two open points of the
measurement.
