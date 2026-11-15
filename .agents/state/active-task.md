# Active task

<!--
What the next session is doing. One task, in the words of the issue that
describes it. Link the issue; do not repeat the whole plan here.
-->

**Issue:** the round of the measured work is released. What is left is the next round: the epic of the audit
([#166](https://github.com/nordmanndev/LUI/issues/166)) with its issues #168–#183 — the connection of the catalogue, the
read-only path, the opening of a catalogue, the freeze of 0.1.0 and the six findings of the audit. Two issues of the round
of the measured work are still open: [#165](https://github.com/nordmanndev/LUI/issues/165) (the file and the line of the
runtime of the one-off) and [#192](https://github.com/nordmanndev/LUI/issues/192) (the window of the unit layer).
**Branch:** `dev` is at `9ba74e3` (the merge of PR #196, the release), `main` at `f4ef3c4` — the tag `v0.2.0` is on the
commit of `main`.
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
- **LUI 0.2.0 is released** (2026-11-15): the tag `v0.2.0` on `main` (the merge commit `f4ef3c4`), the packages
  `Nordmann.LUI` and `Nordmann.LUI.Hosting` at the version `0.2.0` on nuget.org, and the round of the measured work
  with it — six pull requests, every one of them ending in a number. The public API did not move: the freeze of
  [ADR-0026](../adr/0026-the-public-api-from-0-1-0-on-grows-only-additively.md) holds, and there is no migration note.
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
- **The run under load is written and merged** (#154, PR #184, the merge commit `2a492d1`): `tools/load-run.sh` runs the
  four lanes of the suite at once, with the measurement layer beside them, keeps the whole output and the trx of every
  lane, and **names the test that failed** the moment it fails. **The failure named itself**: 30 runs, 120 lanes, six of
  them red — the lost test is `LuiWarmupTests.The_host_waits_for_a_round_that_runs_when_it_is_disposed`, in four of the
  five failing integration runs, and their counts are the counts of the run of 2026-11-13. The reason is measured: the
  claim the test waits for holds for about 1.16 s, and the longest gap between two polls under the load was 1841 ms —
  the round began and ended inside one gap. The numbers stand in `docs/benchmarks.md`. The fix of the test was issue #185 —
  and it is merged (PR #189).
- **The contract with SymSpell is written and merged** (#44, PR #187, the merge commit `dcb3cea`, ADR-0064): the owner
  decided on 2026-11-14 that **`symspell` stays** — maybe to be looked at again another time. The contract is measured:
  `TypoContractTests` pins the rules (the beginnings of three to ten letters, the count of a word = its weight + 1, the
  count threshold 1, the order of the corrections, the head of ten letters) and measures the dictionary over the whole
  word list — 685,739 words, **138 MB**, 2.3 s, and a fingerprint of the corrections that an own implementation would have
  to reproduce. The harness rides on the lane of the parity over the whole word list. The numbers stand in
  `docs/benchmarks.md`.
- **The wait of the warm-up does not expire** (#185, PR #189, the merge commit `f66c584`): the service counts the rounds
  that have **begun** — a claim that stays true — and the round is a **task of the service**, so that the host that stops
  it waits for the task of the round and not for the flag, which is cleared a moment before the round itself is over.
  That is the flake the hunt of #154 named, and the fix of it: the claim of a round that *runs* holds for about a second,
  and a loaded machine delayed the poll of the test by more than that. Two more waits of the same file are fixed with it
  — the round that fails is started by hand and waited for. The mutation
  `a-claim-of-a-round-that-does-not-stay-true` proves that the new guard bites; the promise of ADR-0043 stands as it was
  written, and the record carries a note of it.
- **The write lock of a linked sync is measured, and the rule of ADR-0016 stands** (#31, PR #191, the merge commit
  `421bcd9`): the sync of a linked index of 1,000,000 rows holds the write lock of the file for **26.7 s** — the same
  number as ADR-0015 and ADR-0016 — a writer of the application waits up to **25.8 s**, and with the busy timeout of the
  catalog (5 s, ADR-0046) **4 of 11** of its writes fail with `SQLITE_BUSY`. The owner decided on 2026-11-14: the shadow
  build comes when an application reports the lock as a problem. Nothing is built and nothing changes, so the decision
  gets **no ADR of its own** — a note on ADR-0016 says what it took, the way the notes of the issues #76 and #148 do.
- **The first load keeps its path, and its budget is three minutes again** (#32, PR #194, the merge commit `d125d98`,
  ADR-0065): the lever of the issue — a longer write-ahead log while the first load runs — was measured on two machines
  and **rejected**: 120.0 s → 122.0 s → 120.0 s on the machine of the record, 172.9 s → 180.8 s → 165.1 s on the machine
  of the report, and the log of the file doubles from 229 MB to 424 MB. The other two levers of the issue are **moot**:
  the target holds again, so no lever is needed. The owner decided on 2026-11-15: the budget of the first load goes
  **back to 3 minutes** (180 s) — the promise of ADR-0020 — and the path does not move: `Sync` with the batch of
  1,000,000, the pragmas where SQLite puts them, no lever in the library. The build of the whole corpus of the repository
  (1,908,798 entries) takes **120.4 s** on the machine of the record and **165.1–172.9 s** on the machine of the report.
  Two things had to be right before a number could mean anything, and both were wrong: the lane measured the build with
  the batch of 10,000 (the promise of ADR-0020 names 1,000,000 — worth about 20 %), and the scenario of three million
  entries does not exist — the budget stays reported, not asserted, and the number at three million is the one of
  2026-11-04 (the corpus with three million entries is the task of issue #176).

## Open

The four pull requests of the first round, in the order of the plan:

1. ~~**#137**~~ — the dead paths in the records: six paths in three records. **Done.**
2. ~~**#129**~~ — the index of the ADRs gets a guard. **Done.**
3. ~~**#130**~~ — the API page guards the members, not only the types. **Done.**
4. ~~**#128**~~ — the code blocks of the tutorials are compiled by a guard. The issue names the tutorials by their old
   names; they are `01-first-index.md` … `05-in-the-application.md` since PR #135. **Done.**

Then the release of `0.1.1` — **done** (PR #160, the release pull request #161, the tag `v0.1.1`, the packages on
nuget.org) — and the round of the measured work: **#134** (the 1912 bytes), **#154** (the integration flake), **#44** (the
dependency on `symspell`), **#185** (the wait of the warm-up), **#31** (the write lock of a linked sync), **#32** (the
first load of 3 M entries).

## The next step

**The round of the measured work** — `0.2.0`, on the plan
([the round of the measured work](./../plan/2026-11-13-the-round-of-the-measured-work.md)). Six pull requests, in the order
of the plan:

1. ~~**#134**~~ — the origin of the 1912 bytes inside the measured window: the probe of the window, the runs, the
   finding. **Done** (PR #164, the merge commit `f15370f`) — and the probe caught the event: 87 runs, one dirty window
   that names the iteration and the amount. The open end of it — the file and the line of the runtime — is issue #165.
2. ~~**#154**~~ — the test of the integration layer that failed once under load: the run that keeps the whole output,
   the finding. **Done** (PR #184, the merge commit `2a492d1`) — the failure named itself: 30 runs, 120 lanes, six red,
   and the lost test is `The_host_waits_for_a_round_that_runs_when_it_is_disposed`. The fix of the test is issue #185.
3. ~~**#44**~~ — the dependency on `symspell`: the harness over the whole word list, then the ADR that decides.
   **Done** (PR #187, the merge commit `dcb3cea`, ADR-0064) — the owner decided that `symspell` stays; the harness pins
   the rules of the contract and measures the dictionary over the whole word list: 685,739 words, 138 MB, and the
   fingerprint of the corrections.
4. ~~**#185**~~ — the wait of the warm-up: a claim that stays true, and the task of the round. **Done** (PR #189, the merge
   commit `f66c584`) — the service counts the rounds that have begun, and the host waits for the task of the round. The
   mutation `a-claim-of-a-round-that-does-not-stay-true` proves that the guard bites.
5. ~~**#31**~~ — the write lock of a linked sync: the number of the lock, then the decision. **Done** (PR #191, the merge
   commit `421bcd9`) — the sync of a linked index of 1,000,000 rows holds the write lock of the file for 26.7 s, a writer
   waits up to 25.8 s, and with the busy timeout of the catalog four of eleven writes fail with `SQLITE_BUSY`. The owner
   decided on 2026-11-14: the rule of ADR-0016 stands, and the shadow build comes when an application reports the lock
   as a problem. Nothing is built, so the decision has no ADR of its own — a note on ADR-0016 says what it took.
6. ~~**#32**~~ — a faster first load: the lever that was measured, the numbers of two machines, the budget back at
   3 min, the ADR. **Done** (PR #194, the merge commit `d125d98`, ADR-0065) — the checkpoint interval of the
   write-ahead log is rejected (no gain, and the log of the file doubles); the two other levers of the issue are moot;
   the owner decided on 2026-11-15 that the budget goes **back to 3 minutes** and that the path of the first load does
   not move.

Then the release of `0.2.0` — **done** (PR #196, the release pull request #197, the tag `v0.2.0` on the commit `f4ef3c4`
of `main`, the packages on nuget.org).

**The next round** is the audit of 2026-11-13/14: the epic [#166](https://github.com/nordmanndev/LUI/issues/166) with its
issues #168–#183 — the four records of the epic are ADR-0066 (the catalogue owns the connection), ADR-0067 (the read
path), ADR-0068 (three ways to open a catalogue) and ADR-0069 (the freeze of 0.1.0 is lifted for 0.3.0). Two issues of the
round of the measured work stay open: **#165** (the file and the line of the runtime of the one-off) and **#192** (the
window of the unit layer).
