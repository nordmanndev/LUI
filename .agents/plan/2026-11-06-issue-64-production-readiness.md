# The plan of the production readiness

> An independent architecture review of 2026-11-06 asked the question "is LUI production-ready for Nordmann-Dev?" —
> performance, concurrency, test behaviour and the work of a developer who adopts it. Its verdict: the library is
> **architekturally reif** and **not yet delivered**. It named three gaps that no single commit can close — no
> release, no automatic gate for the budgets of §16, and a typo dictionary that is built inside the read transaction
> of the query — plus one stale number that stands in two documents. This plan is what this round does about the parts
> that belong together: one issue, one pull request, seven tasks, one commit each.

**Issue:** #64 (the epic — one issue, one pull request, the standards' way)
**Branch:** `feat/production-readiness`

## The decisions of the owner (2026-11-06)

1. **The query does not build the typo dictionary any more** — direction 2 of #35. The query answers with
   `CorrectionSkipped` while there is no dictionary; the build belongs to the warm-up, to the writes and to the
   service of task 6. A documented behaviour changes ⇒ ADR-0042, a note in the CHANGELOG, §10 of the specification
   and the README in the same commit.
2. **The scope of the round is the six tasks** — the three heavy performance points of the review (the memory of the
   dictionary, the path of 5–8 characters, the floor of the allocation) are each a change of the query path and get
   their own issue.
3. **`index.As()` is a task of this round** (#59) — it is additive, and the type of the payload belongs to the
   application.
4. **One pull request for the whole plan** — the owner of the repository, 2026-11-06.

## The seven tasks

| | The task | The issue | The decision | The test delta |
|---|---|---|---|---|
| 0 | **The house:** the epic issue, the four sub-issues, the branch, the pull request, this plan, the state of the session | #64 | — | 0 (no code) |
| 1 | **The §16 table of the README and of the benchmarks agrees with the specification — and a guard keeps it there** | #38 | ADR-0040 (the owner's round) | **taken over** — see "What actually happened" |
| 2 | **A lane of the CI checks the budgets of §16** (the allocations are asserted, the times are reported) | #65 | ADR-0041 (Measurement) | +6 integration tests, +1 lane, +1 ADR |
| 3 | **The link check runs in the CI** | #66 | — | 0 new tests, +1 step |
| 4 | **A query does not build the typo dictionary any more** | #35 | ADR-0042 (Typo tolerance) | **+2** integration tests (9 test files), 1 ADR, spec + CHANGELOG |
| 5 | **The concurrency is hardened:** a soak, a second connection on one file, a dispose during a build, a Windows lane | #67 | — | **+3** integration tests, +1 CI lane |
| 6 | **The warm-up is a service of the host — opt-in** (`AddLuiWarmup`) | #68 | ADR-0043 (Runtime) | **+3** integration tests, +1 ADR, `docs/api.md` |
| 7 | **A typed view over the untyped index: `index.As()`** | #59 | ADR-0044 (API) | **+2** integration tests, +1 ADR, `docs/api.md` |

## The checklist

- [x] **Task 0 — The house**
  - [x] The epic issue #64: the seven tasks as acceptance criteria, the list of the sub-issues.
  - [x] The four sub-issues: #65 (task 2), #66 (task 3), #67 (task 5), #68 (task 6).
  - [x] The branch `feat/production-readiness`, cut from `main` at `dd756498`.
  - [x] The pull request, base `main`, head `feat/production-readiness` — **this pull request carries the whole plan**;
        its body is the live checklist. It is #69.
  - [x] This plan in `.agents/plan/`, named after the epic issue.
  - [x] **The state file stays with the session in flight.** `.agents/state/active-task.md` belongs to the session that
        is running; this round keeps its state in this plan, in issue #64 and in the body of the pull request. The file
        was taken out of this branch again — a pull request that overwrites the note of another session is a pull
        request against a dead tree.
  - **Test delta:** 0. **Proof:** the pull request exists and its first CI run is green.

- [x] **Task 1 — The §16 table agrees with the specification (#38)** — **taken over by the owner's pull request #63**
      (`feature/budget-table-guard`, green on 2026-11-06): the guard `Readme_Budgets` reads the target table of §16 out
      of the specification and compares it with the budget table of the README and with the target table of the record;
      it found the real drift (≤ 250 / ≤ 350 KB against the contract's ≤ 60 / ≤ 100 KB) and both tables carry the limits
      of the contract now, with the measured values of the run of 2026-11-06 (46 / 51 KB). The mutation
      `a-budget-limit-in-the-readme-edited-by-hand` gives the guard teeth, ADR-0040 is the decision, and the plan of
      the task is `.agents/plan/2026-11-06-issue-38-the-budget-table-of-the-entrance.md`. **Issue #38 is closed** — the
      proof of the head of #63 (run #111, `60eda3e0`) and of the merge into `main` (run #114, `e30ba166`) stands in it.
      **Nothing of this round touches it** — the issue, the tables, the guard and the ADR belong to that round.

- [x] **Task 2 — A lane of the CI checks the budgets of §16 (#65)**
  - [x] The decision written down: **ADR-0041** — what runs, when, what is asserted against what is only reported.
  - [x] **The rule of the measurement:** the *allocations* are machine-independent and are asserted; the *times* belong
        to the machine they were measured on and are only reported, with the machine named. The rule is one method and
        one enum (`BudgetMachine`), and the unit of the target decides its verdict.
  - [x] A lane of its own: `.github/workflows/performance.yml` — a schedule (nightly) and `workflow_dispatch` with an
        input for the size of the scenario. **Not** a job in `ci.yml`: a job cannot have a schedule of its own.
        *The file stood at `tools/performance-lane.yml` until the owner gave the agent a token — the connector may not
        write into `.github/workflows/`, and a lane outside of it never runs. It stands at
        `.github/workflows/performance.yml` now.*
  - [x] The lane runs the budget test with `LUI_PERF=1` in the scenario of §16 (three million entries, the corpus of the
        specification), and the parity with the gate `LUI_REQUIRE_FULL_WORDLIST=1` — a missing word list makes the lane
        fail instead of measuring a sample and calling it the scenario.
  - [ ] **The measurement of the lane itself:** the first run is triggered by hand and read — how long the build of
        three million entries takes on a runner, whether the disk and the memory hold. The lane is sized by that
        measurement, not by an assumption. **The file is in place now** (`.github/workflows/performance.yml`).
        *A dispatch from the agent's side is impossible, and it is not a matter of trying harder — there are two
        independent causes, and both are measured: the token of the owner reads the runs (`GET /actions/runs` → 200)
        but may not write them (`GET /actions/permissions` → 403, "Resource not accessible by personal access
        token"), and the lane is not registered while it lives only on a branch (`GET
        /actions/workflows/performance.yml` → 404 — a workflow with `workflow_dispatch` must stand on the default
        branch). **The first run therefore happens after the merge** — by the hand of the owner in the tab of the
        actions, or by the agent if the token gets the permission "Actions: write". Meanwhile the steps of the lane
        ran here, in the same order with the same environment and the same filter: the numbers of this machine
        (2 cores, 3 GB) are not the measurement of a runner, they only prove that the lane works.*
  - [x] The gates table in `docs/testing.md` names the lane, and `docs/benchmarks.md` says what a target means on a
        shared runner.
  - **Test delta:** +6 integration tests (`PerformanceBudget_Machine`); the seven targets of §16 move from *never* to
        *nightly*. **Proof:** the red run #117 (exactly one test failed, with the assertion of the missing rule), the
        green run #125 (263 unit, 81 architecture, 436 integration, 43 client, mutation 0 failures), and the first run of
        the lane — **pending** (the file is in place; the run waits for the merge). *The SHAs of the runs name the
        commits of the old history — see "The history of the branch was rebuilt".*

- [x] **Task 3 — The link check runs in the CI (#66)**
  - [x] The check is the tool of the repository (`tools/link-check.py` — 395 links, 0 broken at the time of PR #62) and
        it runs as a **test of the architecture layer**: `Link_Check` starts the tool in the root of the repository and
        asks for its exit code. It runs in every lane of the CI, on every pull request and in the release gate — and
        the developer sees it locally, too.
        *The plan had it as a step of the workflow. A workflow file is a file the agent may not write, and a test needs
        nobody but the branch. The tool stays the tool — the test runs it and asks for its exit code.*
  - [x] The teeth: the red run shows the guard naming the broken link — file, line and target — and exactly one test
        failing. A guard that was never seen red is a guard that proves nothing.
  - [x] The map of the tests names the new file (`Link_Check`, Architecture).
  - [x] **Carry-over:** the mutation `a-link-in-the-documentation-points-at-nothing` in `tools/mutation-check.sh` — it
        is in the branch (`e52bb6c`). It waited for the same reason as the row of ADR-0041: the file of the owner's
        round ends with the mutations of issue #38, and two sides that append at the same place cannot be merged.
        With the base on `main` it came in without a conflict. **Verified here:** the mutation hangs a link that
        points at nothing into `docs/benchmarks.md`, the guard of the architecture layer names it, the link is reset.
  - **Test delta:** +1 architecture test; the links of the documentation are checked by every run of the CI.
        **Proof:** the red run **#129** (head `071893c4`) — one test failed, and the guard named the broken link
        (`docs/benchmarks.md:471: ./this-file-does-not-exist.md does not exist`); the green run **#132** (head
        `9693375f`) — 263 unit, **82 architecture** (the new one among them), 436 integration, 43 client, mutation
        0 failures. *The SHAs name the commits of the old history — see "The history of the branch was rebuilt".*

- [x] **Task 4 — A query does not build the typo dictionary (#35)**
  - [x] **Rot first:** a test that proves a correcting query on an index without a dictionary leaves it *not built* and
        sets `CorrectionSkipped` — the query does not build.
  - [x] The build path leaves the query (`TypoCorrection.IsReady`, `TypoDictionary.TryEnsureBuilt`); the build stays
        where it belongs: `WarmUpTypoDictionary`, `RebuildTypoDictionary`, the writes.
  - [x] The specification (§10.1) and the README are updated in the same commit; the CHANGELOG says what changes.
  - [x] **ADR-0042** — why the query does not build, and what replaces it.
  - [x] `docs/api.md` if a public member disappears — the guard `Public_API_Documentation` says so.
  - **Test delta:** +2 integration tests (the two new ones in `TypoDictionaryLifecycleTests`), **9** test files touched
        (the seven above plus `ReadPathTests` and `LinkedIndexTests`: the tests that want a correction ask for the
        dictionary now). The unit layer keeps its 263 tests: the two tests of `TypoDictionaryStateTests` were renamed,
        because the query that built the dictionary is gone.
        **Proof:** the red run **#134** (head `2299d059`) — exactly two tests failed, the second one after the 2 s busy
        timeout, so the lock conflict is real; the green run **#143** (head `d77f3c16`) — 263 unit, 82 architecture,
        **438** integration (14 skipped), 43 client, the mutation check with 0 failures and the smoke test of the packed
        library green. #35 got the comment and is closed. *The red commit of this task carries the **same patch**
        today (`git patch-id` is equal: `48acd2d3`), the green one differs only in the base.*

- [x] **Task 5 — The concurrency is hardened (#67)**
  - [x] A soak: more readers, more rounds, a bigger corpus — no exception, the counts stay consistent.
  - [x] Two catalogs on one file (two connections): the reader sees a consistent picture while the writer writes.
  - [x] A dispose while a warm-up runs: it ends, it does not hang.
  - [x] A Windows lane in the CI (`windows-latest`): the paths, the pragmas, the case of the file names.
  - [x] The map in `docs/testing.md` names the new tests.
  - **Test delta:** +3 integration tests, +1 CI lane, +2 mutations. **Proof:** run **#149** (head `3645543`) — all five
    jobs green: *Build · Test · Format* (263 unit, 82 architecture, 441 integration + 14 skipped, 43 client), *The
    normalisation parity over the whole word list*, *The guards bite* (both new mutations `CAUGHT BY TEST`, **0
    failures**), *Package · Smoke test* and — for the first time — *Windows · the paths, the pragmas, the names of
    the files* (263 unit, 82 architecture, 441 integration + 14 skipped).

  **What the round found:** the library held up — every new test is green **without a change of the library**. What
  was missing was the proof and the teeth. The teeth are two mutations, and both were seen red before they went
  into the script: the write-ahead log off (`PRAGMA journal_mode = DELETE`) — the writer waits for the lock of the
  reader and fails (`SQLite Error 5: 'database is locked'`); and the dispose that never wakes the waiter (no
  `Monitor.PulseAll`) — the dispose waits for a warm-up that is still running, and neither comes back.

  **The lane of Windows earned its place on its first run.** It found three platform differences that Linux cannot
  see: two guards compared the *bytes* of a file and broke on the CRLF of the checkout (the hash of the vendored
  library, the benchmark tables of the README) — they compare the content now; a test read the bytes of the
  database file while the catalog held it open — on Windows the handle of SQLite denies the read, so the claims
  about the rows and the tables are checked there and the claim about the bytes only where the bytes can be read;
  and the sample's abandoned request had four seconds to be noticed — enough on an empty machine, too little on a
  loaded one.

  **And a bug of the library, which does not belong to this task: issue #70.** The `BusyTimeout` of the catalog does
  not shorten the wait for a lock: with `BusyTimeout = 1 s` a write waits **31 s** — the pragma reads `1000` back
  and the wait is still 31 s, because Microsoft.Data.Sqlite sets `sqlite3_busy_timeout` from the command's timeout
  before every command. The repair needs a decision of its own (a short `CommandTimeout` would interrupt the long
  statements as well), so it has its own issue.

  **A finding of the way, not of the library:** the existing tests of the integration layer carry time windows of
  10 s to 30 s — and one of 300 ms (`CatalogTests.Dispose_waits_for_a_query_that_is_running`). Under a machine
  that runs a measurement of three million entries **beside** the suite, one of them goes red. It is a property of
  the windows, not of the library; the CI runs one job at a time and has four cores. The tests of the round get
  threads of their own — a test that waits for something must not wait for a free thread.


- [x] **Task 6 — The warm-up is a service of the host (opt-in) (#68)**
  - [x] `AddLuiWarmup()` — the outbox is applied at the start, the dictionaries are warmed, the outbox is applied on an
        interval (30 s by default); **off by default**.
  - [x] **ADR-0043** — the hosting package carries the abstractions of the host again (ADR-0034): the rule stands (a
        package carries what it uses), the list changes. It is a service the host registers, not a thread the library
        starts.
  - [x] `docs/api.md` carries the two new methods of the catalog and the new registration.
  - [x] The CHANGELOG (additive), the README (the ASP.NET section), the tutorial (chapter 5).
  - **Test delta:** +4 integration tests, +2 public methods (`WarmUpTypoDictionaries`, `ApplyPending`), +1 registration.
    **Proof:** the red commit `7029503` (2 of 3 cases red, frisch im Worktree nachgeprüft) ⇒ the green run **#154**
    (`988b670`): Unit 263, Architektur 82, Integration 445 + 14 übersprungen, Client 43, `package-smoke-test: OK`,
    Mutations-Check 0 failure(s) — alle fünf Jobs grün.

  **Was die Runde fand:** der **Package-Smoke-Test** erwischte das README — er führt alle C#-Blöcke als **ein Programm**
  aus, und mein zweiter Block registrierte den Katalog ein zweites Mal (`AddLui` wirft). Der Wärmer wanderte in den
  Block des Hosts, wo der Katalog einmal registriert wird.

  Und die **Windows-Lane** fand ein Rennen, das Task 5 offen gelassen hatte (#67): der Dispose-Test wartete auf den
  Zustand des Wörterbuchs mit `await Task.Delay(10)` — und die Fortsetzung des `await` braucht einen freien Thread des
  Pools. Unter der Last der Suite kam sie so spät, dass der Build fertig war (`Current` statt `Building`) und der
  Dispose den Katalog unter dem Warm-up schloss. Die Warteschleife schläft jetzt auf dem Thread des Tests, und die
  Behauptung „der Dispose kommt während des Warm-ups" wird über die **Zeitstempel** der beiden Worker bewiesen statt
  gehofft.

- [ ] **Task 7 — A typed view over the untyped index (#59)**
  - [ ] `index.As<T>()` — a thin, allocation-free view that delegates to the one index and calls `GetPayload<T>()` for
        the caller; the index keeps no type, the file keeps no type name.
  - [ ] The open questions of #59 are decided in **ADR-0044**: the name is `As<T>()`, the key stays `LuiKey` (a second
        typed key view would be a second concept), and the payload is **not** cached (a cache per hit is memory and an
        invalidation).
  - [ ] `docs/api.md` carries the new public types — otherwise `Public_API_Documentation` fails the build.
  - [ ] The CHANGELOG (additive — allowed by ADR-0026), the README, the tutorial.
  - **Test delta:** +2 integration tests. **Proof:** the CI run; #59 gets the comment and is closed.

## What actually happened

The ground moved while the plan was written — the owner opened pull request #63 (`feature/budget-table-guard`) at
2026-11-06 23:07 UTC, green at 23:12, and took **issue #38** with it: the guard for the budget table, the correction
of both tables, the mutation and **ADR-0040**. Task 1 is therefore not done twice: the round of #63 is the round of the
budget table, and this plan keeps its place in the history. Two consequences for this round:

- the ADR numbers move by one — the lane of the CI is **ADR-0041**, the dictionary **ADR-0042**, the warm-up
  **ADR-0043**, the typed view **ADR-0044**;
- **the state file belongs to the session in flight.** `.agents/state/active-task.md` is the owner's; this branch does
  not touch it, and the state of this round lives in this plan, in #64 and in the body of the pull request.

### The history of the branch was rebuilt (2026-11-07)

The owner gave the agent a token. That changed the ground under the work in three ways, and all three are written
down here instead of being worked around in silence:

1. **The work happens with a real git** — a clone, a branch, a push with the token in the header of the request and
   never in the URL of the remote, never in `.git/config` and never in a commit.
2. **The branch was rebuilt on the base of `main`** (`e30ba166`): 32 commits, one thing per commit. The six identical
   duplicates (the push of the agent had run three times) and the six probe commits are gone — they were noise in a
   history, not work.
3. **The content of the round did not change.** `git diff --stat d77f3c16 e52bb6c` (the green head of task 4 in the
   old history against the head of today) names seven files: the round of the owner (#63 — `Readme_Budgets.cs` and its
   plan), the carry-over of the round (the two rows of the ADR index, the mutation, the lane file) and the text of this
   plan. **The source, the tests and the documents of the round differ by not a single line.**

⇒ **The SHAs of the commits of the round have changed; the runs stay.** A run is the proof — a SHA is only the
address of a commit. The runs #117, #125, #129, #132, #133, #134, #143 and #144 are the red and the green phases of
the tasks; run **#145** (`e52bb6c`) is green on the branch as it stands now.

### The carry-over is done (2026-11-07)

The three things that waited for the base are in the branch (`e52bb6c`): the rows **0041** and **0042** in the index
of the decisions, the mutation `a-link-in-the-documentation-points-at-nothing` and the lane file — which stands at
`.github/workflows/performance.yml` now, because the token lets the agent move it there itself. **A lane outside of
`.github/workflows/` never runs**: the file at `tools/performance-lane.yml` was a file, not a lane.

## The steps of every task

The same eight steps, every time — the order is part of the contract:

1. **The issue** carries the task (#64 for the round; #65, #66, #35, #67, #68 and #59 for the tasks — #38 belongs to
   the round of the owner, pull request #63).
2. **Rot first**: the new rule gets its test, and the test is seen red before the fix.
3. The fix — the code, the documentation, the specification, the CHANGELOG, the ADR: **in the same commit**.
4. The map of the tests (`docs/testing.md`) carries the new test file — it is a guard.
5. The push on `feat/production-readiness` — **one commit per task**, no force push.
6. The CI run of the commit is read; its output is the verification (the real output, not the words "I ran the tests").
7. The **issue** gets the comment with the proof — before/after, the test delta, the HEAD SHA, the run — and is closed.
8. The **pull request body** carries the checklist and the verification; **the owner gets the feedback of the task, and
   the next task waits for a word.**

## The assumptions

1. **One pull request for the whole plan** — the owner's decision of 2026-11-06. It stays one issue (#64) and one
   pull request, so the house rule ("one pull request per issue") holds.
2. **The merge is the owner's.** Nothing of this round is merged by me.
3. **The public API is not broken.** Task 4 changes a *documented behaviour* — before the tag `v0.1.0` that is
   allowed, but it needs an ADR and a note in the CHANGELOG. Tasks 6 and 7 are additive. No task changes the schema
   or the dependency set.
4. **The work happens with a real git** — since 2026-11-07 the owner gave the agent a token: a clone, a branch, a
   push. The token never lands in the URL of the remote, in `.git/config` or in a commit — the authentication goes
   through the header of the request. The test suite runs here as well (the .NET SDK of the channel 10.0), so the
   **proof of every task is the CI run of the pushed commit**, read from the logs of the run — and the local run
   before the push.
5. **`main` moves during the round.** Before every push the head of `main` is read; a file that changed there is
   re-read and carried over, never overwritten — a pull request against a dead tree is more dangerous than no pull
   request.
6. **The budgets of §16 stay as they are.** This round does not move a target: it makes the targets *automatic*
   (task 2) and *consistent* (task 1).

## What is deliberately not in this pull request

| | The point of the review | Why not here | Where it goes |
|---|---|---|---|
| 1 | The typo dictionary costs 138 MB per index and per process | A new representation of the word beginnings is a project of its own, with a measurement of its own | its own issue, after 0.1.0 |
| 2 | The path of 5–8 characters is 13,4–17,5 ms | A word-start table beside FTS5 is a change of the query path — the riskiest kind of change in this repository | its own issue, after 0.1.0 |
| 3 | The floor of the allocation: the texts of the candidates (32–64 B each) | It needs the UTF-8 handles of the provider — an experiment first, a decision second | its own issue |
| 4 | The first load of three million entries (172–206 s) | Issue #32 says "after 0.1.0" | #32 |
| 5 | The dependency on `symspell` | Issue #44 says "after 0.1.0" | #44 |
| 6 | A shadow build for `Linked.Sync()` | Issue #31 says "after 0.1.0" | #31 |

**Verified by:** the CI run of every commit (the unit, the architecture and the integration layer, the mutation check,
the package smoke test, the link check, the sample client) — read from the logs of the run; the comment on the issue of
the task carries it.
