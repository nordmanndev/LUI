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
| 4 | **A query does not build the typo dictionary any more** | #35 | ADR-0042 (Typo tolerance) | **+2** integration tests, 1 ADR, spec + CHANGELOG |
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
        *The file stands at `tools/performance-lane.yml` until the owner moves it — the connector of the agent may not
        write into `.github/workflows/`. One command: `git mv tools/performance-lane.yml .github/workflows/performance.yml`.*
  - [x] The lane runs the budget test with `LUI_PERF=1` in the scenario of §16 (three million entries, the corpus of the
        specification), and the parity with the gate `LUI_REQUIRE_FULL_WORDLIST=1` — a missing word list makes the lane
        fail instead of measuring a sample and calling it the scenario.
  - [ ] **The measurement of the lane itself:** the first run is triggered by hand and read — how long the build of
        three million entries takes on a runner, whether the disk and the memory hold. The lane is sized by that
        measurement, not by an assumption. **It waits for the owner:** the file has to move first.
  - [x] The gates table in `docs/testing.md` names the lane, and `docs/benchmarks.md` says what a target means on a
        shared runner.
  - **Test delta:** +6 integration tests (`PerformanceBudget_Machine`); the seven targets of §16 move from *never* to
        *nightly*. **Proof:** the red run #117 (exactly one test failed, with the assertion of the missing rule), the
        green run #125 (263 unit, 81 architecture, 436 integration, 43 client, mutation 0 failures), and the first run of
        the lane — **pending**.

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
  - [ ] **Carry-over:** the mutation `a-link-in-the-documentation-points-at-nothing` in `tools/mutation-check.sh`. It
        waits for the same reason as the row of ADR-0041: the file of the owner's round ends with the mutations of
        issue #38, and two sides that append at the same place cannot be merged. It comes with the base.
  - **Test delta:** +1 architecture test; the links of the documentation are checked by every run of the CI.
        **Proof:** the red run **#129** (head `071893c4`) — one test failed, and the guard named the broken link
        (`docs/benchmarks.md:471: ./this-file-does-not-exist.md does not exist`); the green run **#132** (head
        `9693375f`) — 263 unit, **82 architecture** (the new one among them), 436 integration, 43 client, mutation
        0 failures.

- [ ] **Task 4 — A query does not build the typo dictionary (#35)**
  - [ ] **Rot first:** a test that proves a correcting query on an index without a dictionary leaves it *not built* and
        sets `CorrectionSkipped` — the query does not build.
  - [ ] The build path leaves the query (`TypoCorrection.IsReady`, `TypoDictionary.TryEnsureBuilt`); the build stays
        where it belongs: `WarmUpTypoDictionary`, `RebuildTypoDictionary`, the writes.
  - [ ] The specification (§10.1) and the README are updated in the same commit; the CHANGELOG says what changes.
  - [ ] **ADR-0042** — why the query does not build, and what replaces it.
  - [ ] `docs/api.md` if a public member disappears — the guard `Public_API_Documentation` says so.
  - **Test delta:** +2 integration tests, the existing `TypoDictionaryLifecycleTests` brought to the new contract.
        **Proof:** the CI run; #35 gets the comment and is closed.

- [ ] **Task 5 — The concurrency is hardened (#67)**
  - [ ] A soak: more readers, more rounds, a bigger corpus — no exception, the counts stay consistent.
  - [ ] Two catalogs on one file (two connections): the reader sees a consistent picture while the writer writes.
  - [ ] A dispose while a warm-up runs: it ends, it does not hang.
  - [ ] A Windows lane in the CI (`windows-latest`): the paths, the pragmas, the case of the file names.
  - [ ] The map in `docs/testing.md` names the new tests.
  - **Test delta:** +3 integration tests, +1 CI lane. **Proof:** the CI run of the commit (both runners).

- [ ] **Task 6 — The warm-up is a service of the host (opt-in) (#68)**
  - [ ] `AddLuiWarmup()` — the dictionaries are warmed at the start, the outbox is brought in; **off by default**.
  - [ ] **ADR-0043** — the hosting package still carries only the dependency injection (ADR-0034); this is a service the
        host registers, not a thread the library starts.
  - [ ] `docs/api.md` carries the new public types — otherwise `Public_API_Documentation` fails the build.
  - [ ] The CHANGELOG (additive), the README (the ASP.NET section), the tutorial.
  - **Test delta:** +3 integration tests. **Proof:** the CI run.

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
4. **The work happens over the Contents API** — there is no PAT and no clone in this environment. The commits get fresh
   SHAs, the test suite cannot be run here, and **the proof of every task is the CI run of the pushed commit**, read
   from the logs of the run.
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
