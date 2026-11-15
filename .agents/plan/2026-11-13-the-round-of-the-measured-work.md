# Plan — The round of the measured work: 0.2.0

- **Date:** 2026-11-13
- **Base branch:** `dev`
- **Decided by:** the owner, on 2026-11-13, after the release of `0.1.1` — the two forks of the round (#31, #44) were put to
  the owner and answered: **measure first, then decide**
- **Issues:** #32, #31, #44, #134, #154, #185 — #117 (the epic they belong to)
- **Status:** in progress — six pull requests of it are merged (#134, PR #164, the merge commit `f15370f`; #154, PR #184,
  the merge commit `2a492d1`; #44, PR #187, the merge commit `dcb3cea`; #185, PR #189, the merge commit `f66c584`;
  #31, PR #191, the merge commit `421bcd9`; #32, PR #194, the merge commit `d125d98`) — what is left is the release
- **Why now:** the tag `v0.1.1` exists (2026-11-13), the round of the guards is released, and what is left is the work that
  needs numbers before it needs code

## 1. The goal

**0.2.0 — the round of the measured work.** Six tasks, and four of them are questions that the repository cannot answer
without a number:

1. **The first load of three million entries** (#32) — the target of the original is 3 min (180 s): it holds on one machine
   and misses on the next, with the same code (172.5 s and 194.9–205.9 s). The budget was raised to 4 min on that day
   (ADR-0020). The levers are known, and none of them is measured at that size. **The owner answered on 2026-11-15:** the
   budget goes back to 3 min — the lever that was measured brings no gain, and the target holds again (ADR-0065).
2. **The write lock of a linked sync** (#31) — 26.7 s for a million rows, in one transaction. ADR-0016 left the question to
   the owner: *is a thirty-second lock per million rows a problem worth it?* The owner answered it on 2026-11-14: the
   rule stands, and the shadow build comes when an application reports the lock as a problem.
3. **The dependency on `symspell`** (#44) — the last foreign algorithm of the core. The dependency set of a package is its
   contract (ADR-0018), so the question is the owner's; the data to answer it is ours to measure.
4. **The 1912 bytes** (#134) — one allocation in one measured window of one run, never reproduced.
5. **The integration test that failed once** (#154) — under load, in a run whose output was filtered, and the name of the
   test was lost with it.
6. **The wait of the warm-up** (#185) — the test that the hunt of #154 named waits for a round that *runs*, a claim that
   holds for about a second, and a loaded machine misses it. The fix is the round's own task and a claim that stays true.

The order is the order of the costs: the two flakes first (they need runs, not decisions), then the fix of the flake that
the hunt named (it needs a claim that does not expire), then the two decisions (the measurements behind them are cheap),
then the load (it needs two machines and hours), then the release.

## 2. What the release of 0.1.1 left open

The release of `0.1.1` closed the four guards of the round before it and left these five open. They are of three kinds, and
the kinds need different things of a pull request.

| Kind | The issues | What it is | What a pull request costs |
|---|---|---|---|
| **Questions about the code** | #134, #154 | two flakes, both with the facts known and the cause unknown | a probe, a procedure, and the number of the runs |
| **Fixes of the code** | #185 | the wait of the warm-up for a round that runs — a claim that holds for a second | a claim that stays true, the task of the round, and a mutation |
| **Questions about the design** | #31, #44 | the lock of a linked sync, the dependency on `symspell` | a measurement, then an ADR |
| **Work that is measured** | #32 | the first load of three million entries | four levers at 3 M on two machines, the path in `Sync`, an ADR |

The epic #117 waits for two of them: #137 (the dead paths of the records ✓ done) and #134.

## 3. The decisions

| # | The decision | Why |
|---|---|---|
| D1 | **The next version is `0.2.0`, a minor one**, and it carries the six tasks of this round. | The freeze of ADR-0026 is a promise about the API: a break needs a minor version, a migration note and an ADR. Nothing here breaks the API, but the round is of the weight of a minor version — a new ADR about the dependency set, and possibly a new mode of `Sync` (`LuiSyncOptions`) — and a patch would understate it. |
| D2 | **The round measures first and decides with an ADR** — for #31 and #44 the implementation is *not* in this round. | The owner answered both forks on 2026-11-13 with the same words: *measure first, then decide*. The shadow build is a second write path for every refresh of a linked index; an own SymSpell has to be bit-exact against the one that is measured today (ADR-0012: 4.2 s, 138 MB, 685,739 words, and the floors of `TypoQualityTests`). Neither is a matter of taste, and both have a number that decides them. |
| D3 | **#32 is the one implementation of the round**: the chosen path goes into `Sync`, with an ADR. | The budget `build-3m` (≤ 4 min, ADR-0056) held — 172.5–205.9 s on two machines — but the target of the original is 3 min, and it held on one machine and missed on the next. A decision without a change would leave the promise where it is; the ADR says whether the chosen path brings the 3 min back. **It does not: no path changes** (ADR-0065). |
| D4 | **The order is by the cost of the work**: the flakes, the fix of the flake, the two decisions, the load, the release. | The flakes need runs and nothing else; the fix of the flake needs a claim that does not expire; the two decisions need one number each; the load needs two machines and hours, and its ADR may change the API. |
| D5 | **The two flakes are answered by keeping the evidence, not by guessing.** The probe of the allocation window is a tool of the repository (its buffer is allocated *before* the window, its printing *after* it), and the run under load keeps the whole output — the name of the test is written down the moment it fails. | The issue #154 says it plainly: an issue that names a test on suspicion sends the next agent after the wrong thing. The one failure that was lost was lost because the output was filtered; the fix is a procedure that does not filter. |
| D6 | **The numbers of the round go into the report with the machine beside them** (`docs/benchmarks.md`), and a time belongs to the machine it was measured on (ADR-0041). | The same code built three million entries in 172.5 s and in 205.9 s on two machines — a number without its machine says nothing. ADR-0020 measured the build on two machines for that reason. |
| D7 | **Every decision of the round that changes the public API, the schema or the dependency set gets its ADR in the same pull request.** The ADRs of the round are the dependency on `symspell` and the first load; the shadow build of ADR-0016 gets no ADR of its own — the rule of ADR-0016 stands, and a note on that record says what it took. | The rule of the repository (AGENTS.md). The dependency set is the contract of the package (ADR-0018), and a budget that is missed is measured again and decided again, with an ADR and a record — it is not lowered in passing. A decision that changes nothing needs no record of its own: a note on the record it belongs to says what it took. |
| D8 | **The branch model does not change.** The pull requests target `dev`; `dev` → `main` is a release pull request with its own review; the tag `v0.2.0` follows it, and the owner approves the push to nuget.org. | The same road as `0.1.0` and `0.1.1` — the machinery is proved a third time, at low cost. |

## 4. The order: six pull requests and a release

The first three pull requests are the spine of the round (D1, D5, D2). The fourth is the fix of the flake that the hunt of
the second pull request named: a test that waits for a round that *runs* waits for a claim that holds for about a second,
and a loaded machine misses it. The fifth and the sixth carry the two measurements that are left — the lock of a linked
sync (#31) and the first load of three million entries (#32) — and then the release.

### PR 1 — The 1912 bytes (#134, D5)

- **Files:** the probe of the measured window — a tool of the repository, next to `tools/mutation-check.sh` — and the
  dispatch of the lane on the branch that carries it. The workflow can be started on any branch without a pull request
  (`POST /repos/nordmanndev/LUI/actions/workflows/ci.yml/dispatches`).
- **The rule:** the probe allocates its buffer **before** the window and prints **after** it. A probe that prints inside the
  window measures itself — the interpolated string and the console buffer allocate, and every number is the probe's own.
  The per-iteration deltas go into the folder the job uploads.
- **The trap:** the probe has to be *armed*, not run once — the allocation happened once, in one run, out of many. The
  number of the runs is part of the evidence.
- **The acceptance:** a run in which the window is not clean, with the per-iteration deltas, and the source named — a file
  and a line of the runtime — **or** the documented statement that it cannot be reproduced, with the number of the runs.
  The epic #117 closes with it.

**What the pull request did (2026-11-14).** Two things differ from the plan above, and both are recorded here rather than
in the plan:

- The **probe is in the unit layer** — the rule it watches is the rule of the unit layer, and a probe beside the rule
  measures the same window — and the **tool next to `tools/mutation-check.sh` is the driver**: it dispatches the runs,
  collects their reports and says how many runs there were.
- The runs do **not** happen on the branch that carries the probe: the CI cancels a run of the same ref (the group of
  its concurrency is the ref), and the run of the pull request is not the run of the probe. The driver pushes the commit
  of the branch to temporary branches, dispatches there, and deletes them again.
- **The probe caught the event**: run 43534600454, the first window, iteration 62, 7312 bytes. The source is not the
  library (the rule's own test was green in that run) and not one of the suspects the issue excluded — what is left is
  the one-off of the runtime's own machinery that the issue named. The file and the line of the runtime are not named:
  that needs the allocation sampling of the runtime, and it is an issue of its own.

### PR 2 — The integration test that failed once (#154, D5)

- **Files:** the procedure of the run under load — the integration layer while the performance layer takes the machine, with
  the whole output kept, and the result of the run kept (the `trx` of the lane, not a grep over it).
- **The rule:** the run that reproduces a failure has to name the test. Everything else is a guess.
- **The trap:** the failure came in a run where all four layers ran **at once** — the runs of the layer alone were green.
  A lane on its own runner has no load at all; the load is local, or it is made.
- **The acceptance:** one run in which the failure names itself and the reason is written down — **or** the documented
  answer of "it does not happen any more", with the number of the runs that says so.

**What the pull request did (2026-11-14).** The failure named itself, and the name is the one the issue did not have:

- The procedure is **`tools/load-run.sh`**: the **four lanes of the suite at once**, with the measurement layer beside
  them. The plan said "the integration layer while the performance layer takes the machine"; the run of that day was the
  whole suite in one run, and the four lanes at once are what makes the machine busy the way it was busy that day.
- **30 runs, 120 lanes, six of them red** — five of the integration layer, one of the unit layer. The lost test is
  `LuiWarmupTests.The_host_waits_for_a_round_that_runs_when_it_is_disposed` (four of the five), and the counts of the
  failing runs are the counts of the run of 2026-11-13: `Failed: 1, Passed: 421, Skipped: 1, Total: 423`. The issue
  suspected the warm-up teardown of #148 and wrote it off; it is that test.
- **The reason**, measured with an instrumentation that is not in the repository: the test waits for a claim that holds
  only while a round runs, and the round gives up after the busy timeout of one second that the test itself sets — it
  lives about 1.16 s. The wait polls every 20 ms; under the load the longest gap between two polls was **1841 ms**, so
  the round began and ended inside one gap and the claim was never seen. The same test has a second shape of the same
  race: the task that wraps the round completes a moment after the flag that says "no round runs".
- **The fix is not in this pull request** — it is an issue of its own: a claim that holds for a second cannot be waited
  for under a load.

### PR 3 — The dependency on `symspell`: the harness, then the decision (#44, D2, D7)

- **Files:** the harness that says **what LUI asks of `symspell`** — the delete variants over the word beginnings, the
  frequencies (`weight + 1`), the prefix length 7, the count threshold 1, the order of the suggestions — measured over the
  whole word list; and the ADR.
- **The rule:** the harness is a characterisation, not a re-implementation: it pins the behaviour that an own
  implementation would have to match, on the whole word list, and it is the number the decision is made with. It rides on
  the lane of the parity over the whole word list, where the list is there.
- **The trap:** the floors of `TypoQualityTests` (55 / 70 / 80 / 85 / 88 % of the right beginning for 4 … 8 letters) and
  the budget of the dictionary (≤ 10 s, ≤ 200 MB) are the bar an own implementation has to clear — the harness has to say
  whether it clears it, and it cannot say so before the implementation exists. If the ADR says "own it", the next round
  writes the implementation against this characterisation and moves it into the library.
- **The acceptance:** the harness runs over the whole word list and its result is in the report; the ADR decides whether
  `symspell` stays — and if it goes, it names the round that implements it.

**What the pull request did (2026-11-14).** The decision is made, and it is the owner's: **`symspell` stays** (ADR-0064) —
maybe to be looked at again another time, with numbers instead of an impression.

- The harness is `TypoContractTests`: the **rules** of the contract on a corpus with known weights (the beginnings of
  three to ten letters, the count of a word = its weight + 1, the count threshold 1, the order of the corrections, the
  head of ten letters), and the **measurement** over the whole word list — the dictionary of LUI itself, built over
  685,739 words: **138 MB**, 2.3 s, 684,416 of the words corrected, and a **fingerprint** of the corrections
  (`936f9a72…`) that an own implementation would have to reproduce. The fingerprint is the same in every run.
- The harness rides on the lane of the parity over the whole word list — where the list is — and the mutation
  `a-count-threshold-that-drops-the-light-words` proves that the rules bite.
- **A difference from the plan.** The plan named the delete variants and the prefix length 7 among the things the harness
  says. Neither is observable in the corrections: at the distance 1 the candidates of a lookup are the words whose first
  seven letters are near, and the distance decides — the reach of the correction is the head of ten letters, and the
  prefix length is a **cost**, not a behaviour. The harness therefore pins the rules that are observable, and it measures
  the cost (138 MB) and the behaviour (the fingerprint). The 880,384 delete variants of the record of 2026-11-12 stay a
  number of that record: they are not reproducible from the repository, and re-deriving them would be the
  re-implementation the plan says the harness is not.

### PR 4 — The wait of the warm-up: a claim that stays true, and the task of the round (#185, D5)

- **Files:** the service of the warm-up (`src/Nordmann.LUI.Hosting/Hosting/LuiWarmupService.cs`) and the tests of the
  warm-up (`tests/Nordmann.LUI.Integration.Tests/Hosting/LuiWarmupTests.cs`).
- **The rule:** the claim of a round that **runs** holds only while the round runs, and the round of that test lives about
  a second — a waiter that comes late must not miss it. The service counts the rounds that have **begun**: a claim that
  stays true. And the round is a **task of the service**, so that the host that stops it waits for the task of the round
  and not for the flag, which is cleared a moment before the round itself is over.
- **The trap:** the wait of a test that polls a transient claim is a deadline in disguise. The test of the round that
  fails waits for a warning of the logger — and the round of the timer can bring the row of the outbox in before the
  table of the application is renamed under it, so that the failure never comes at all. The round of that test is started
  by hand and waited for.
- **The acceptance:** the claim stays true after the round is over (a guard with a mutation that proves it bites), and
  the host waits for the task of the round.

**What the pull request did (2026-11-14).** Both rules are in the service of the warm-up: the count of the rounds that
have begun (`RoundsBegun`) and the task of the round (`RoundTask`), which the host that stops it waits for. The three
waits of the file ask for the claim that stays true, and the round that fails is started by hand and waited for — the
round of the timer can bring the row of the outbox in before the table of the application is renamed under it, and then
the failure never comes at all. The mutation `a-claim-of-a-round-that-does-not-stay-true` proves that the new guard
bites; the four mutations of the warm-up that were there before still bite. The public API does not move — the service is
internal — and ADR-0043 carries a note, like the note of issue #148.

### PR 5 — The write lock of a linked sync: the number, then the decision (#31, D2, D7)

- **Files:** the measurement — a writer of the application's table beside a linked sync, in the performance layer — the
  record (`docs/benchmarks.md`), and the note on ADR-0016.
- **The rule:** the question of ADR-0016 is *"is a thirty-second lock per million rows a problem worth it?"* — an
  application answers it, not a document. A writer writes to the table of the application while the index syncs, and
  reports how long it waits.
- **The trap:** the shadow build is not only the build: a `Refresh` or an `ApplyPending` that runs during the build writes
  the old tables, and those writes have to reach the new ones too (the outbox, or a second write path). That is the reason
  the decision is not taken without a number — and the number is about the lock, not about the build.
- **The acceptance:** the number of the wait on the machine of the measurement, with the machine named; the decision says
  whether the shadow build is built, and if it is, it names the round that builds it.

**What the pull request did (2026-11-14).** The measurement lives in the performance layer, not in the sample application:
the sample demonstrates the library, and the layer of the measurements is where "what does this cost" lives (ADR-0058,
ADR-0019). The number: the sync of a linked index of 1,000,000 rows holds the write lock of the file for **26.7 s** — the
same number as ADR-0015 and ADR-0016 — a writer waits up to **25.8 s**, and with the busy timeout of the catalog (5 s) **4
of 11** of its writes fail with `SQLITE_BUSY`. The owner decided on 2026-11-14: **the rule of ADR-0016 stands**, and the
shadow build comes when an application reports the lock as a problem. Nothing is built and nothing changes — the public
API, the schema and the dependency set stay as they are — so the decision gets **no ADR of its own**: a note on ADR-0016
says what it took, the way the notes of issues #76 and #148 do.

### PR 6 — The faster first load (#32, D3, D6, D7)

- **Files:** the levers measured at the size of the corpus of the repository, the record (`docs/benchmarks.md`) with the
  machine beside each number, the budget file (`tests/Nordmann.LUI.Testing/Budgets/budgets.json`) and the ADR.
- **The levers** (the issue names them; the numbers of ADR-0016 are at 1 M):
  - **the checkpoint interval of the WAL** while a sync loads — 12 % at 1 M, for a WAL of up to 400 MB, not taken;
  - **the content table in key order** — the keys are in order today; the question is whether the three columns of the
    table (`s_rowid`, `s_p1`, `s_p2`) and the FTS5 index over them are cheaper to build after the load than during it;
  - **`PRAGMA synchronous`** during the first load — risky, and only if the owner wants it;
  - the batch size: the build already runs with `BatchSize = 1_000_000` (ADR-0020), the fewest commits — that lever is
    taken.
- **The trap:** the fourth lever of the issue — *"an empty index in one transaction, with content first, then rebuild"* —
  is **measured already and rejected** (ADR-0016): 48.3 s against 42.9 s for large sorted batches, it needs the triggers
  gone across the transactions, an aborted sync would leave the index without them, and it cannot be run again. It stays the
  repair (`Rebuild()`), not the load. The plan does not measure it a third time.
- **The acceptance:** each lever measured at 3 M on at least two machines; the chosen path in `Sync` with an ADR; the
  build holds the 3 min of the original target again on the machine of the measurement, or the ADR says why not. The
  budget of 4 min stays as it is — a promise is not lowered in passing, and it is not raised without a record.

**What the pull request did (2026-11-15).** Two things had to be right before a number could mean anything, and both were
wrong at first: **the lane measured the build with the batch of 10,000**, while the promise of ADR-0020 names the batch of
1,000,000 — the batch is worth about 20 % (ADR-0016), so the number the lane published was not the number of the promise;
and **the scenario of three million entries does not exist** — since ADR-0059 the corpora are files in the repository, and
the words corpus holds 1,908,798 entries, so a budget below its scenario is reported, not asserted. The lane and the
default of the scenario file now carry the batch of ADR-0020.

The **lever that was measured** is the checkpoint interval of the write-ahead log, and it is **rejected**: on the machine of
the record 120.0 s without it, 122.0 s with a log of 100,000 pages, 120.0 s again without it; on the machine of the report
172.9 s, 180.8 s, 165.1 s. No gain — and the log of the file doubles (229 MB → 424 MB). The other two levers of the issue
are **moot**: the target holds again, so no lever is needed.

The **owner decided on 2026-11-15**: the budget of the first load goes **back to 3 minutes** (180 s) — the promise of
ADR-0020 — and **the path of the first load does not move**: `Sync` with the batch of 1,000,000, the pragmas where SQLite
puts them, no lever in the library. The build of the whole corpus of the repository (1,908,798 entries) takes **120.4 s** on
the machine of the record and **165.1–172.9 s** on the machine of the report. The decision has an ADR of its own
(ADR-0065), because a promise that moves is a promise: the file of the budgets says 180 s again, and the tables of the
README and of the record follow it (the guard `ReadmeBudgetsTests` holds the three together).

**What stays open:** the number at three million entries is the one of 2026-11-04 (172.5 / 174.2 s on one container,
194.9 / 205.9 s on the next) — the corpus that has three million entries, and the limits measured against it, are the task
of issue #176.

### PR 7 — The release `0.2.0`

- The version line (`VersionPrefix` `0.2.0`), the section `## [0.2.0] — <date>` of the CHANGELOG with a migration note if
  the API moved, the README (the badge of the version, the install line, the Status), the pull request `dev` → `main` with
  its own review, the tag `v0.2.0`, and the owner's approval of the push to nuget.org.
- **A minor version:** the freeze of ADR-0026 allows an additive change without a note, and a break with a note. The CHANGELOG
  says which of the two this release is.
- **The trap of the release:** the merge of the release pull request can take `dev` with it — its head *is* `dev`. On
  2026-11-15 the branch was gone from the remote after PR #197 was merged: the button **Delete branch** next to the merge
  of a pull request deletes its head, and the head of the release pull request is `dev`. Nothing was lost (the history is
  in the merge commits, and `dev` was recreated at the commit of `main`), but the state pull request that followed was
  closed because its base was gone. **After the merge of the release pull request: do not click "Delete branch" there, and
  check that `dev` is still there** — the next round branches from `dev`.

## 5. What 0.2.0 holds, and what it does not

| The task | What the round does | What it does not do |
|---|---|---|
| #134 — the 1912 bytes | the probe of the window, the runs, the finding | it does not promise a cause it has not seen |
| #154 — the integration flake | the procedure of the run under load, the whole output, the finding | it does not name a test on suspicion |
| #185 — the wait of the warm-up | the claim of a round that has begun, the task of the round, the guard and its mutation | it does not move the promise of ADR-0043 — the note on the record says what it took |
| #44 — `symspell` | the harness over the whole word list, the ADR | it does not write the own implementation — the ADR names the round that does |
| #31 — the shadow build | the number of the lock, the note on ADR-0016 | it does not build the shadow tables — the decision names the application that reports the lock as the trigger |
| #32 — the first load | the lever that was measured, the numbers of two machines, the budget back at 3 min, the ADR | it does not lower the budget in passing, and it does not raise it without a record — and it does not put a lever into the library that brings nothing |
| #117 — the epic | it closes with #134 | the weekly lane that says whether the budgets hold goes on |

## 6. The traps that are known

- **A budget that is missed is not lowered in passing.** The build of 3 M is ≤ 3 min (ADR-0020, the file of the budgets
  ADR-0056) again — the owner decided that on 2026-11-15, and the ADR of it (ADR-0065) and the record say what it took.
  A target that is reached is raised with an ADR and a record; a target that is missed is measured again and decided
  again, and not written down as met.
- **A time belongs to the machine it was measured on** (ADR-0041). The same code took 172.5 s and 205.9 s on two machines
  a day apart — the build writes a 925 MB file and follows the disk. Every number of the round names its machine.
- **The measurement lane is not part of the CI.** A budget on a shared runner is a coin toss (ADR-0020); the lane of the
  schedule runs the numbers of the week (Mondays, `performance.yml`), and the numbers of the round are measured on the
  machine of the report.
- **The word list is not in the repository.** The corpora are (ADR-0059); the German word list (685,829 words) is fetched
  (`tools/fetch-wordlist.sh`) — the harness of PR 3 rides on the lane of the parity over the whole word list, where it is.
- **The map of the tests is a guard.** A new test file without a row in `docs/testing.md` fails the build
  (`TestLandscapeTests`). The harness of PR 3 adds a test file, so it adds a row.
- **The mutation check runs in four shards.** A new mutation takes a place in the rotation; the shards must stay balanced,
  or a shard runs no mutation at all and the script says so.
- **The format check is the CI, not the local machine.** `dotnet format --verify-no-changes` on Windows reports the line
  endings of the checkout; the CI on Linux is the authority. Nothing in this plan reformatts a file.
- **The version line is the tag.** The publish pipeline reads `VersionPrefix` and, if there is one, `VersionSuffix`; the tag
  `v0.2.0` has to agree with it, or the run refuses the tag.
- **The findings of the performance audit of 2026-11-13/14 are not in this round.** They belong to the epic #166 (0.3.0),
  which says that its code is not touched before 0.2.0 is done. The cheap ones among them (#181, #182, #175) would have
  been easy here, but a round does not own the work of the next one — and #175 (the limit of the two-error dictionary,
  240 MB → 260 MB) is decided by the owner and waits for its pull request in 0.3.0.
- **The numbers of the records of the 0.3.0 epic shift by one.** ADR-0064 is the decision about `symspell` (merged on
  2026-11-14), so the four records of the epic are ADR-0066–ADR-0069: the pragmas (0066), the read-only path (0067), the
  catalog that writes nothing (0068) and the freeze (0069). ADR-0065 is the first load of #32 (2026-11-15), and the epic
  and its issues name the new numbers.

## 7. What is not decided here

- **Whether the shadow build is built** (#31) — the ADR of PR 4 decides it, with the number of the lock in hand.
- **Whether `symspell` stays** (#44) — the ADR of PR 3 decides it, with the harness over the whole word list in hand.
- **The source of the 1912 bytes** (#134) — until a run catches it live; the acceptance accepts the documented "it cannot be
  reproduced", with the number of the runs.
- **Which test of the integration layer failed** (#154) — until a run names itself; the acceptance accepts the documented
  "it does not happen any more", with the number of the runs.
- **The order of the three ADRs** — they follow the order of the pull requests, and the numbers of the records follow the
  index.

## 8. The record

| The decision | It lives in |
|---|---|
| D1 (the version) | this plan; the freeze is ADR-0026, the version is ADR-0053, the release is ADR-0048 |
| D2 (measure first, then decide) | this plan; the owner's answers of 2026-11-13 |
| D3 (#32 is the one implementation) | this plan; the budget is ADR-0020 and ADR-0056 |
| D4 (the order) | this plan |
| D5 (the evidence, not the guess) | this plan; the rules of the measurement are ADR-0058 and ADR-0050 |
| D6 (the numbers with their machine) | this plan; ADR-0041 and ADR-0020 |
| D7 (an ADR with every decision) | AGENTS.md; the dependency set is ADR-0018 |
| D8 (the branch model) | this plan; the release is ADR-0048 |

The round changes the public API only if the path of #32 needs a new option, and then only additively. The two decisions
(#31, #44) get their ADR with the number that decided them, and the implementation they may call for belongs to the round
after this one.
