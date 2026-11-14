# Plan — The round of the measured work: 0.2.0

- **Date:** 2026-11-13
- **Base branch:** `dev`
- **Decided by:** the owner, on 2026-11-13, after the release of `0.1.1` — the two forks of the round (#31, #44) were put to
  the owner and answered: **measure first, then decide**
- **Issues:** #32, #31, #44, #134, #154 — #117 (the epic they belong to)
- **Status:** in progress — the first pull request of it is written (#134, the branch `feat/allocation-probe`)
- **Why now:** the tag `v0.1.1` exists (2026-11-13), the round of the guards is released, and what is left is the work that
  needs numbers before it needs code

## 1. The goal

**0.2.0 — the round of the measured work.** Five tasks, and four of them are questions that the repository cannot answer
without a number:

1. **The first load of three million entries** (#32) — the target of the original is 3 min (180 s): it holds on one machine
   and misses on the next, with the same code (172.5 s and 194.9–205.9 s). The budget was raised to 4 min on that day
   (ADR-0020) and it holds today. The levers are known, and none of them is measured at that size.
2. **The write lock of a linked sync** (#31) — 26.7 s for a million rows, in one transaction. ADR-0016 left the question to
   the owner: *is a thirty-second lock per million rows a problem worth it?*
3. **The dependency on `symspell`** (#44) — the last foreign algorithm of the core. The dependency set of a package is its
   contract (ADR-0018), so the question is the owner's; the data to answer it is ours to measure.
4. **The 1912 bytes** (#134) — one allocation in one measured window of one run, never reproduced.
5. **The integration test that failed once** (#154) — under load, in a run whose output was filtered, and the name of the
   test was lost with it.

The order is the order of the costs: the two flakes first (they need runs, not decisions), then the two decisions (the
measurements behind them are cheap), then the load (it needs two machines and hours), then the release.

## 2. What the release of 0.1.1 left open

The release of `0.1.1` closed the four guards of the round before it and left these five open. They are of three kinds, and
the kinds need different things of a pull request.

| Kind | The issues | What it is | What a pull request costs |
|---|---|---|---|
| **Questions about the code** | #134, #154 | two flakes, both with the facts known and the cause unknown | a probe, a procedure, and the number of the runs |
| **Questions about the design** | #31, #44 | the lock of a linked sync, the dependency on `symspell` | a measurement, then an ADR |
| **Work that is measured** | #32 | the first load of three million entries | four levers at 3 M on two machines, the path in `Sync`, an ADR |

The epic #117 waits for two of them: #137 (the dead paths of the records ✓ done) and #134.

## 3. The decisions

| # | The decision | Why |
|---|---|---|
| D1 | **The next version is `0.2.0`, a minor one**, and it carries the five tasks of this round. | The freeze of ADR-0026 is a promise about the API: a break needs a minor version, a migration note and an ADR. Nothing here breaks the API, but the round is of the weight of a minor version — a new ADR about the dependency set, and possibly a new mode of `Sync` (`LuiSyncOptions`) — and a patch would understate it. |
| D2 | **The round measures first and decides with an ADR** — for #31 and #44 the implementation is *not* in this round. | The owner answered both forks on 2026-11-13 with the same words: *measure first, then decide*. The shadow build is a second write path for every refresh of a linked index; an own SymSpell has to be bit-exact against the one that is measured today (ADR-0012: 4.2 s, 138 MB, 685,739 words, and the floors of `TypoQualityTests`). Neither is a matter of taste, and both have a number that decides them. |
| D3 | **#32 is the one implementation of the round**: the chosen path goes into `Sync`, with an ADR. | The budget `build-3m` (≤ 4 min, ADR-0056) holds today — 172.5–205.9 s on two machines — but the target of the original is 3 min, and it holds on one machine and misses on the next. A decision without a change would leave the promise where it is; the ADR says whether the chosen path brings the 3 min back. |
| D4 | **The order is by the cost of the work**: the flakes, the two decisions, the load, the release. | The flakes need runs and nothing else; the two decisions need one number each; the load needs two machines and hours, and its ADR may change the API. |
| D5 | **The two flakes are answered by keeping the evidence, not by guessing.** The probe of the allocation window is a tool of the repository (its buffer is allocated *before* the window, its printing *after* it), and the run under load keeps the whole output — the name of the test is written down the moment it fails. | The issue #154 says it plainly: an issue that names a test on suspicion sends the next agent after the wrong thing. The one failure that was lost was lost because the output was filtered; the fix is a procedure that does not filter. |
| D6 | **The numbers of the round go into the report with the machine beside them** (`docs/benchmarks.md`), and a time belongs to the machine it was measured on (ADR-0041). | The same code built three million entries in 172.5 s and in 205.9 s on two machines — a number without its machine says nothing. ADR-0020 measured the build on two machines for that reason. |
| D7 | **Every decision of the round that changes the public API, the schema or the dependency set gets its ADR in the same pull request.** The three ADRs of the round are the shadow build, the dependency on `symspell`, and the first load. | The rule of the repository (AGENTS.md). The dependency set is the contract of the package (ADR-0018), and a budget that is missed is measured again and decided again, with an ADR and a record — it is not lowered in passing. |
| D8 | **The branch model does not change.** The pull requests target `dev`; `dev` → `main` is a release pull request with its own review; the tag `v0.2.0` follows it, and the owner approves the push to nuget.org. | The same road as `0.1.0` and `0.1.1` — the machinery is proved a third time, at low cost. |

## 4. The order: five pull requests and a release

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

### PR 4 — The write lock of a linked sync: the number, then the decision (#31, D2, D7)

- **Files:** the sample application — a writer that reports the lock as a problem — the measurement, and the ADR.
- **The rule:** the question of ADR-0016 is *"is a thirty-second lock per million rows a problem worth it?"* — an
  application answers it, not a document. The sample writes to its table while the index syncs, and reports how long the
  writer waits.
- **The trap:** the shadow build is not only the build: a `Refresh` or an `ApplyPending` that runs during the build writes
  the old tables, and those writes have to reach the new ones too (the outbox, or a second write path). That is the reason
  the decision is not taken without a number — and the number is about the lock, not about the build.
- **The acceptance:** the number of the wait on the machine of the measurement, with the machine named; the ADR decides
  whether the shadow build is built, and if it is, it names the round that builds it.

### PR 5 — The faster first load (#32, D3, D6, D7)

- **Files:** the levers measured at 3 M, the chosen path in `Sync` (`src/Nordmann.LUI/Indexes/`), the ADR, and the report
  (`docs/benchmarks.md`) with the machine beside each number.
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

### PR 6 — The release `0.2.0`

- The version line (`VersionPrefix` `0.2.0`), the section `## [0.2.0] — <date>` of the CHANGELOG with a migration note if
  the API moved, the README (the badge of the version, the install line, the Status), the pull request `dev` → `main` with
  its own review, the tag `v0.2.0`, and the owner's approval of the push to nuget.org.
- **A minor version:** the freeze of ADR-0026 allows an additive change without a note, and a break with a note. The CHANGELOG
  says which of the two this release is.

## 5. What 0.2.0 holds, and what it does not

| The task | What the round does | What it does not do |
|---|---|---|
| #134 — the 1912 bytes | the probe of the window, the runs, the finding | it does not promise a cause it has not seen |
| #154 — the integration flake | the procedure of the run under load, the whole output, the finding | it does not name a test on suspicion |
| #44 — `symspell` | the harness over the whole word list, the ADR | it does not write the own implementation — the ADR names the round that does |
| #31 — the shadow build | the number of the lock, the ADR | it does not build the shadow tables — the ADR names the round that does |
| #32 — the first load | four levers at 3 M on two machines, the path in `Sync`, the ADR | it does not lower the budget of 4 min in passing, and it does not raise it without a record |
| #117 — the epic | it closes with #134 | the weekly lane that says whether the budgets hold goes on |

## 6. The traps that are known

- **A budget that is missed is not lowered in passing.** The build of 3 M is ≤ 4 min (ADR-0020, the file of the budgets
  ADR-0056) and it holds today; the target of #32 is the 3 min of the original, and a target that is reached is raised with
  an ADR and a record.
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
