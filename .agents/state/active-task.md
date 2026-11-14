# Active task

<!--
What the next session is doing. One task, in the words of the issue that
describes it. Link the issue; do not repeat the whole plan here.
-->

**Issue:** #154 — a test of the integration layer failed once under load, and the name of it was lost. It is the second
pull request of the round of the measured work.
**Branch:** `feat/154-the-run-under-load` carries the run under load; `dev` is at `c89fe85` (the merge of PR #167), `main`
at `320605e` — the tag `v0.1.1` is on the commit of `main`.
**Decision:** the plan
([the round of the measured work](./../plan/2026-11-13-the-round-of-the-measured-work.md)) — D1 to D8, on the owner's
behalf, 2026-11-13.

## What is done

- **LUI 0.1.0 is released** (2026-11-13): the tag `v0.1.0` on `main`, the packages `Nordmann.LUI` and
  `Nordmann.LUI.Hosting` at the version `0.1.0`, and the freeze of
  [ADR-0026](../adr/0026-the-public-api-from-0-1-0-on-grows-only-additively.md) in force — the public API grows only
  additively from this version on.
- **LUI 0.1.1 is released** (2026-11-13): the tag `v0.1.1` on `main`, the packages at the version `0.1.1`, the four guards
  of the round and the dead paths of the records. The public API did not move.
- **The whole series is merged**: the public API of the review of 2026-11-04 (ADR-0063), the route named in the call
  (ADR-0062), the chapter that says why the tests are built this way (#81), the warm-up that leaves nothing running
  (#148), and the repository English throughout (ADR-0057).
- **The epics #39, #64 and #72 are closed** with the release.
- **The plan of the round is written**: `0.1.1` carries the six dead paths of the records and the three missing guards;
  `0.2.0` carries the measured work and the decisions.
- **The six dead paths of the records are repaired**, and a guard holds them up: every path of the list of the affected
  files of a current record names a file that exists.
- **The three missing guards of the round are there**: the paths of the records (#137), the index of the records (#129), the
  members of the API page (#130) and the C# blocks of the tutorial (#128).
- **The probe of the measured window is written and merged** (#134, PR #164, the merge commit `f15370f`): the unit lane
  can be dispatched with the probe armed, the probe names the iteration of a dirty window that allocated and the amount,
  and `tools/allocation-probe.sh` dispatches the runs, collects their reports and says how many runs there were.
  **The probe caught the event** on the runner: 87 runs, one dirty window — the first window, iteration 62, 7312 bytes.
  The numbers stand in `docs/benchmarks.md`. The file and the line of the runtime are the open end of it: issue #165.
- **The run under load is written** (#154, PR #168): `tools/load-run.sh` runs the four lanes of the suite at once, with the
  measurement layer beside them, keeps the whole output and the trx of every lane, and **names the test that failed** the
  moment it fails. **The failure named itself**: 30 runs, 120 lanes, six of them red — the lost test is
  `LuiWarmupTests.The_host_waits_for_a_round_that_runs_when_it_is_disposed`, in four of the five failing integration runs,
  and their counts are the counts of the run of 2026-11-13. The reason is measured: the claim the test waits for holds for
  about 1.16 s, and the longest gap between two polls under the load was 1841 ms — the round began and ended inside one
  gap. The numbers stand in `docs/benchmarks.md`. The fix is an issue of its own.

## Open

The four pull requests of the first round, in the order of the plan:

1. ~~**#137**~~ — the dead paths in the records: six paths in three records. **Done.**
2. ~~**#129**~~ — the index of the ADRs gets a guard. **Done.**
3. ~~**#130**~~ — the API page guards the members, not only the types. **Done.**
4. ~~**#128**~~ — the code blocks of the tutorials are compiled by a guard. The issue names the tutorials by their old
   names; they are `01-first-index.md` … `05-in-the-application.md` since PR #135. **Done.**

Then the release of `0.1.1` — **done** (PR #160, the release pull request #161, the tag `v0.1.1`, the packages on
nuget.org) — and the round of the measured work: **#134** (the 1912 bytes), **#154** (the integration flake), **#44** (the
dependency on `symspell`), **#31** (the shadow build of `Linked.Sync()`), **#32** (the first load of 3 M entries).

## The next step

**The round of the measured work** — `0.2.0`, on the plan
([the round of the measured work](./../plan/2026-11-13-the-round-of-the-measured-work.md)). Five pull requests, in the order
of the plan:

1. ~~**#134**~~ — the origin of the 1912 bytes inside the measured window: the probe of the window, the runs, the
   finding. **Done** (PR #164, the merge commit `f15370f`) — and the probe caught the event: 87 runs, one dirty window
   that names the iteration and the amount. The open end of it — the file and the line of the runtime — is issue #165.
2. **#154** — the test of the integration layer that failed once under load: the run that keeps the whole output, the
   finding. **Written** — the failure named itself: 30 runs, 120 lanes, six red, and the lost test is
   `The_host_waits_for_a_round_that_runs_when_it_is_disposed`.
3. **#44** — the dependency on `symspell`: the harness over the whole word list, then the ADR that decides.
4. **#31** — the shadow build for `Linked.Sync()`: the number of the write lock in the sample, then the ADR that decides.
5. **#32** — a faster first load: the levers at 3 M on two machines, the path in `Sync`, the ADR.

Then the release of `0.2.0`: the version line, the section of the CHANGELOG, the README, the pull request `dev` → `main`,
the tag `v0.2.0`, and the owner's approval of the push to nuget.org.
