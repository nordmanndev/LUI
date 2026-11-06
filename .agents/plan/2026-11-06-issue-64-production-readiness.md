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
   service of task 6. A documented behaviour changes ⇒ ADR-0041, a note in the CHANGELOG, §10 of the specification
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
| 1 | **The §16 table of the README and of the benchmarks agrees with the specification — and a guard keeps it there** | #38 | the issue is the record | **+1** architecture test, +1 map row, +1 mutation |
| 2 | **A lane of the CI checks the budgets of §16** (the allocations are asserted, the times are reported) | #65 | ADR-0040 (Measurement) | 0 new tests, +1 lane, +1 ADR |
| 3 | **The link check runs in the CI** | #66 | — | 0 new tests, +1 step |
| 4 | **A query does not build the typo dictionary any more** | #35 | ADR-0041 (Typo tolerance) | **+2** integration tests, 1 ADR, spec + CHANGELOG |
| 5 | **The concurrency is hardened:** a soak, a second connection on one file, a dispose during a build, a Windows lane | #67 | — | **+3** integration tests, +1 CI lane |
| 6 | **The warm-up is a service of the host — opt-in** (`AddLuiWarmup`) | #68 | ADR-0042 (Runtime) | **+3** integration tests, +1 ADR, `docs/api.md` |
| 7 | **A typed view over the untyped index: `index.As()`** | #59 | ADR-0043 (API) | **+2** integration tests, +1 ADR, `docs/api.md` |

## The checklist

- [ ] **Task 0 — The house**
  - [x] The epic issue #64: the seven tasks as acceptance criteria, the list of the sub-issues.
  - [x] The four sub-issues: #65 (task 2), #66 (task 3), #67 (task 5), #68 (task 6).
  - [x] The branch `feat/production-readiness`, cut from `main` at `dd756498`.
  - [ ] The pull request, base `main`, head `feat/production-readiness` — **this pull request carries the whole plan**;
        its body is the live checklist.
  - [x] This plan in `.agents/plan/`, named after the epic issue.
  - [ ] `.agents/state/active-task.md` — the state of the round, per `AGENTS.md` ("Session start").
  - **Test delta:** 0. **Proof:** the pull request exists and its first CI run is green.

- [ ] **Task 1 — The §16 table agrees with the specification (#38)**
  - [ ] **Rot first:** a new architecture test reads the target table of §16 in the specification and compares it with
        the limit column of the README's budget table and of the one in `docs/benchmarks.md`. Seen red before the fix:
        a limit edited by hand ⇒ the test fails.
  - [ ] The stale row corrected: the limits of ADR-0032 (**p50 ≤ 60 KB, p95 ≤ 100 KB**), and no pointer to #30, which
        has been closed since 2026-11-05.
  - [ ] The measured column carries a number that exists — the measured values of ADR-0032 (p50 18,09 KB, p95 38,80 KB
        at 200.000 entries), named with the scenario they were measured in.
  - [ ] The new test in the map (`docs/testing.md`) — the map is a guard; an unlisted test file fails the build.
  - [ ] A mutation in `tools/mutation-check.sh`: a limit in the README edited by hand ⇒ the guard bites.
  - **Test delta:** +1 architecture test, +1 map row, +1 mutation. **Proof:** the CI run of the commit; #38 gets the
        comment and is closed.

- [ ] **Task 2 — A lane of the CI checks the budgets of §16 (#65)**
  - [ ] The decision written down: **ADR-0040** — what runs, when, what is asserted against what is only reported.
  - [ ] **The rule of the measurement:** the *allocations* are machine-independent and are asserted; the *times* belong
        to the machine they were measured on and are only reported, with the machine named.
  - [ ] A job `performance` in `.github/workflows/ci.yml`: a schedule (nightly) and `workflow_dispatch` with an input
        for the size of the scenario.
  - [ ] The job runs the budget test with `LUI_PERF=1` in the scenario of §16 (three million entries, the corpus of the
        specification).
  - [ ] **The measurement of the lane itself:** the first run is triggered by hand and read — how long the build of
        three million entries takes on a runner, whether the disk and the memory hold. The lane is sized by that
        measurement, not by an assumption.
  - [ ] The gates table in `docs/testing.md` names the lane.
  - **Test delta:** 0 new tests; the seven assertions of §16 move from *never* to *nightly*. **Proof:** the triggered
        run, its log, the numbers.

- [ ] **Task 3 — The link check runs in the CI (#66)**
  - [ ] One step in the build job: `python3 tools/link-check.py` — 395 links, 0 broken at the time of PR #62.
  - [ ] The exit code of the tool decides: a broken link or a broken anchor fails the build.
  - **Test delta:** 0 new tests, +1 step. **Proof:** the CI run; a broken link in the branch fails it.

- [ ] **Task 4 — A query does not build the typo dictionary (#35)**
  - [ ] **Rot first:** a test that proves a correcting query on an index without a dictionary leaves it *not built* and
        sets `CorrectionSkipped` — the query does not build.
  - [ ] The build path leaves the query (`TypoCorrection.IsReady`, `TypoDictionary.TryEnsureBuilt`); the build stays
        where it belongs: `WarmUpTypoDictionary`, `RebuildTypoDictionary`, the writes.
  - [ ] The specification (§10.1) and the README are updated in the same commit; the CHANGELOG says what changes.
  - [ ] **ADR-0041** — why the query does not build, and what replaces it.
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
  - [ ] **ADR-0042** — the hosting package still carries only the dependency injection (ADR-0034); this is a service the
        host registers, not a thread the library starts.
  - [ ] `docs/api.md` carries the new public types — otherwise `Public_API_Documentation` fails the build.
  - [ ] The CHANGELOG (additive), the README (the ASP.NET section), the tutorial.
  - **Test delta:** +3 integration tests. **Proof:** the CI run.

- [ ] **Task 7 — A typed view over the untyped index (#59)**
  - [ ] `index.As<T>()` — a thin, allocation-free view that delegates to the one index and calls `GetPayload<T>()` for
        the caller; the index keeps no type, the file keeps no type name.
  - [ ] The open questions of #59 are decided in **ADR-0043**: the name is `As<T>()`, the key stays `LuiKey` (a second
        typed key view would be a second concept), and the payload is **not** cached (a cache per hit is memory and an
        invalidation).
  - [ ] `docs/api.md` carries the new public types — otherwise `Public_API_Documentation` fails the build.
  - [ ] The CHANGELOG (additive — allowed by ADR-0026), the README, the tutorial.
  - **Test delta:** +2 integration tests. **Proof:** the CI run; #59 gets the comment and is closed.

## The steps of every task

The same eight steps, every time — the order is part of the contract:

1. **The issue** carries the task (#64 for the round; #38, #65, #66, #35, #67, #68 and #59 for the tasks).
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
