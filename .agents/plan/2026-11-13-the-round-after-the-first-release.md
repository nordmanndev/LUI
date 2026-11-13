# Plan — The round after the first release: the guards, the dead paths and the measured work

- **Date:** 2026-11-13
- **Base branch:** `dev`
- **Decided by:** the owner, on 2026-11-13, after the release of `0.1.0`
- **Issues:** #137, #130, #129, #128 (the first round) — #134, #32, #31, #44 (the second) — #117 (the epic they belong to)
- **Status:** the first round is written — four pull requests (#137, #129, #130, #128); the release of `0.1.1` is next
- **Why now:** the tag `v0.1.0` exists (2026-11-13), the freeze of ADR-0026 is in force, and what the release left open is
  the work of this plan

## 1. The goal

Two versions, one plan.

1. **0.1.1 — the promises the repository makes about itself.** Four tasks, and none of them touches the library: the
   six paths of the records that point at files that moved, and the three rules of the repository that nothing checks
   (the API page holds up the types but not their members, the index of the ADRs is written by hand, the code blocks of
   the tutorials are compiled by nothing).
2. **0.2.0 — the measured work and the decisions.** The first load of three million entries, the shadow build of
   `Linked.Sync()`, the dependency on `symspell`, and the 1912 bytes inside the measured window.

The order is the one of the costs: the guards first, because they are what keeps the rest of the work honest, the
measured work second, because it needs the numbers of a release that is out.

## 2. What the release found

The release of `0.1.0` closed three epics (#39, #64, #72) and left nine issues open. They fall into three kinds, and
the kinds need different things of a pull request.

| Kind | The issues | What it is | What a pull request costs |
|---|---|---|---|
| **Records that point at nothing** | #137 | six paths in four ADRs name files that moved with PR #77 and PR #136 | a table of six rows and a guard |
| **Guards that are missing** | #130, #129, #128 | three rules of the repository that no lane of the CI checks | a test, a row of the map, a mutation |
| **Work that is measured, not decided** | #134, #32, #31, #44 | the questions of the measurement and of the dependency set | a measurement, a decision, an ADR |

A fourth thing has no issue and no name: during the verification of #81 an integration test failed once and did not
reproduce in sixteen runs (twelve runs of the layer, four runs of the whole suite). The facts are known and small — it
happened once, under load, and the name was lost with the output of that run.

## 3. The decisions

| # | The decision | Why |
|---|---|---|
| D1 | **The next version is `0.1.1`, and it carries the four tasks of the first round.** No public API, no behaviour, no ADR. | ADR-0026 freezes the API from the tag: a change that breaks it costs a minor version, a migration note and an ADR. The four tasks break nothing, so they cost a patch. |
| D2 | **The version after that is `0.2.0`, and it carries the measured work.** | The freeze is a promise about the API; the next version is where the promise is either kept or broken with a note. A new mode of `Sync` is additive and belongs there; a shadow build is internal; the dependency set is the contract of the package (ADR-0018) and needs an ADR of its own. |
| D3 | **Every guard comes with the proof that it bites.** Each of the three new guards gets a mutation, and `tools/mutation-check.sh` has to catch it. | A guard that never fires is a line of code that costs a second to read and nothing else (ADR-0019). |
| D4 | **The mutation check learns a third mechanism:** `expect_command_caught <name> <command>` — a mutation that a *command* has to catch, not a test. The tutorial check is a command and not a test (issue #128), and the framework knows only dotnet tests today. | The alternative is a proof that lives in the body of a pull request and dies with it. The mechanism is ~15 lines, and it is the tooling that grows, not the library. |
| D5 | **The paths of the records are repaired, the names stay.** #137 changes six paths in four ADRs; the class names stay as they are written — ADR-0049 is the key to the old names. | A path has no such key: a path that names a file that does not exist is a broken promise, a class name is history. |
| D6 | **The flake gets an issue with the two facts that are known and nothing else.** | An issue that says "it happened once, I do not know which test" is honest and actionable; an issue that guesses is neither. |
| D7 | **The branch model does not change.** The pull requests target `dev`; `dev` → `main` is a release pull request with its own review; the tag `v0.1.1` follows it, and the owner approves the push to nuget.org. | The same road as `0.1.0` — the release machinery is proved a second time, at low cost. |

## 4. The order: four pull requests and a release

### PR 1 — The dead paths in the records (#137, D5)

- **Files:** `.agents/adr/0032-*.md` (line 161), `.agents/adr/0041-*.md` (lines 9 and 10), `.agents/adr/0056-*.md`
  (lines 14, 15 and 16) — six paths, six rows of the table of the issue.
- **The paths that are named today and where they live:**
  `tests/Nordmann.LUI.Integration.Tests/PerformanceBudgets.cs` →
  `tests/Nordmann.LUI.Performance.Tests/BudgetPerformance.cs` ·
  `tests/Nordmann.LUI.Integration.Tests/PerformanceBudget.cs` →
  `tests/Nordmann.LUI.Testing/PerformanceBudget.cs` ·
  `tests/Nordmann.LUI.Integration.Tests/PerformanceBudget_Machine.cs` →
  `tests/Nordmann.LUI.Unit.Tests/Budgets/BudgetMachineTests.cs` ·
  `tests/Nordmann.LUI.Integration.Tests/Performance/PerformanceBudget.cs` →
  `tests/Nordmann.LUI.Testing/PerformanceBudget.cs` ·
  `tests/Nordmann.LUI.Integration.Tests/Performance/BudgetPerformance.cs` →
  `tests/Nordmann.LUI.Performance.Tests/BudgetPerformance.cs` ·
  `tests/Nordmann.LUI.Integration.Tests/Performance/BudgetMachineTests.cs` →
  `tests/Nordmann.LUI.Unit.Tests/Budgets/BudgetMachineTests.cs`
- **The guard:** every path in the lists of affected files of `.agents/adr/*.md` exists in the tree. The link check
  (`tools/link-check.py`) does not see them today — a path in a list of code is not a link. The guard belongs to the
  architecture layer, next to `RepositoryLayoutTests`, which reads the tree already.
- **The acceptance:** the six paths are repaired; a path that names a file that does not exist fails the build; the
  mutation check catches a path that is broken again; `docs/testing.md` has a row for the new test file; the four test
  projects are green.

### PR 2 — The index of the ADRs gets a guard (#129)

- **Files:** a new test in the architecture layer — `tests/Nordmann.LUI.Architecture.Tests/AdrIndexTests.cs` — and a
  row of the map.
- **The rule:** every file of `.agents/adr/` has a row in `.agents/adr/README.md`, and every row of that index names a
  file that exists. The index is written by hand today, so a new ADR without a row is invisible until a reader looks
  for it.
- **The acceptance:** the rule is a test of the architecture layer; the mutation check catches an ADR without a row and
  a row without an ADR; the map has the row; the four test projects are green.

### PR 3 — The API page guards the members, not only the types (#130)

- **Files:** `tests/Nordmann.LUI.Architecture.Tests/PublicApiDocumentationTests.cs` — the file that holds the page up
  against the assemblies today. The guard of the members belongs next to the guard of the types.
- **The rule:** every member of the public API that the page names exists on the type, with the signature the page
  gives. The page named `TypoTolerance` on a type that has `TypoDistance`, and the guard accepted it, because it
  compared types and not members.
- **The acceptance:** the rule bites; the mutation check catches a member of the page that the type does not have; the
  map has the row; the four test projects are green.

### PR 4 — The code blocks of the tutorials are compiled (#128, D4)

- **Files:** `tools/tutorial-check.py`, next to `tools/package-smoke-test.sh` — the tool of the repository is Python
  (`tools/link-check.py`), and the work of this one is to read markdown and to write a C# project; the lane of the CI that
  runs it; the mutation check gains `expect_command_caught`.
- **The rule:** every C# block of `docs/tutorial/*.md` is extracted, written into a program, built and run. The
  README's blocks are compiled by the package smoke test today; the tutorials' blocks are compiled by nothing.
- **The trap:** the issue names the tutorials by their old names (`docs/tutorial/01-erster-index.md`); they are
  `01-first-index.md` … `05-in-the-application.md` since PR #135. The issue is corrected when it is picked up — this is
  the same defect as #137, in the issue instead of in a record.
- **The acceptance:** the script fails on a broken block and passes on the tree; the mutation check catches a broken
  block through the new mechanism; the lane of the CI runs it on every pull request; the four test projects are green.

### PR 5 — The release `0.1.1`

- The version line (`VersionPrefix` `0.1.1`, without a suffix), the section `## [0.1.1] — <date>` of the CHANGELOG, the
  README (the badge of the version, the install line, the Status), the pull request `dev` → `main` with its own review,
  the tag `v0.1.1`, and the owner's approval of the push to nuget.org.
- **No ADR:** the release is decided (ADR-0048, ADR-0053, ADR-0026). A patch that changes no public API needs none.

## 5. What 0.2.0 holds, and what it does not

The second round is **not** decided here — it is named, so that the first round is not mistaken for the whole work.

| The task | What it needs | What it costs |
|---|---|---|
| #134 — the 1912 bytes inside the measured window | one dispatch of the CI on a branch with the diagnostic: the per-iteration deltas, printed outside the measured window | a question, not a version |
| #32 — a faster first load | the four levers measured at 3 M entries on two machines; the budgets of the new number | a new mode of `Sync` is additive API → `0.2.0` |
| #31 — a shadow build of `Linked.Sync()` | an application that reports the lock as a problem; a writer that does not wait 26 s | internal → `0.2.0`, no ADR |
| #44 — `symspell` or SymSpell ourselves | the owner: the dependency set is the contract of the package (ADR-0018) | an ADR → `0.2.0` |
| #117 — the epic of the surface and the measurement | #137 and #134, and the weekly lane that says whether the budgets hold on the release machine | the epic closes with them |

## 6. The traps that are known

- **The map of the tests is a guard.** A new test file without a row in `docs/testing.md` fails the build
  (`TestLandscapeTests`). Every pull request of this plan adds a test file, so every pull request adds a row.
- **The mutation check runs in four shards.** A new mutation takes a place in the rotation; the shards must stay
  balanced, or a shard runs no mutation at all and the script says so.
- **The format check is the CI, not the local machine.** `dotnet format --verify-no-changes` on Windows reports the
  line endings of the checkout; the CI on Linux is the authority. Nothing in this plan reformatts a file.
- **The tutorial check builds C#.** The lane needs the SDK of the runner, and it must not run inside the measured
  window of the performance lane.
- **The version line is the tag.** The publish pipeline reads `VersionPrefix` and, if there is one, `VersionSuffix`; the
  tag `v0.1.1` has to agree with it, or the run refuses the tag.

## 7. What is not decided here

- Which of the three guards comes first — the plan names the order, and the owner may change it.
- Whether the shadow build of `Linked.Sync()` is worth it at all (issue #31 asks for an application that reports the
  lock).
- The measure of the 1912 bytes, until #134 has caught one (the question is open in the issue).
- The dependency of the package, until #44 is decided.

## 8. The record

| The decision | It lives in |
|---|---|
| D1, D2 (the versions) | this plan; the freeze itself is ADR-0026, the version is ADR-0053 |
| D3, D4 (the proofs and the third mechanism of the mutation check) | this plan; the mutation check is ADR-0019, the map is ADR-0019 |
| D5 (the paths, the names) | this plan; the key to the old names is ADR-0049 |
| D6 (the flake) | the issue it gets |
| D7 (the branch model) | this plan; the release is ADR-0048 |

The four tasks of the first round change no public API, so they get no ADR — a decision about the work that changes no
code of the library is a plan, not a record of a decision.
