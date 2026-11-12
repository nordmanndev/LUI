# 0058 — The measurement is a layer of its own

- **Status:** Accepted
- **Date:** 2026-11-12
- **Builds on:** [0019](./0019-the-test-tree-three-layers-one-map.md) (the test tree),
  [0041](./0041-the-budgets-of-16-run-on-a-schedule.md) (the budgets of §16),
  [0050](./0050-the-tests-that-read-the-clock-carry-a-trait-and-argue-their-bounds.md) (the trait of the clock),
  [0056](./0056-the-budgets-live-in-a-file.md) (the budgets live in a file)
- **Affects:**
  - `tests/Nordmann.LUI.Performance.Tests/` (new) — the six files that read the clock, and the scenario file
  - `tests/Nordmann.LUI.Testing/PerformanceBudget.cs`, `BudgetMachine.cs`, `BudgetTargetMissedException.cs`,
    `FullWordListFactAttribute.cs` (new)
  - `tests/Nordmann.LUI.Unit.Tests/Budgets/BudgetMachineTests.cs` (moved from the integration layer)
  - `tests/Nordmann.LUI.Architecture.Tests/TestLandscapeTests.cs`, `TimingTraitTests.cs`
  - `.github/workflows/performance.yml`, `.github/workflows/ci.yml`, `tools/fetch-wordlist.sh`
  - `docs/testing.md`, `docs/benchmarks.md`, `docs/normalization.md`, `docs/typo-dictionary.md`
- **Issues:** #122 (epic #64)

## Context

Six files of the integration layer read the clock: `BudgetPerformance`, `QueryPerformance`, `SyncPerformance`,
`TypoPerformance`, `LinkedPerformance`, `BatchPerformance`. They are not tests of behaviour — they are the numbers of
[`docs/benchmarks.md`](../../docs/benchmarks.md). Three things were wrong with the place they lived in:

1. **They ran when somebody remembered a variable.** A variable of the environment switched them on; without it they
   were skipped with a reason. The switch was invisible: it stood in no file, in no report of a run and in no diff. A
   measurement that is off by default is a measurement that is off — and a reader of the tree could not see that it
   existed at all. The names of the variables stand in D18 of the paper of 2026-11-11.
2. **They were mixed into the layer of the behaviour.** The integration layer holds the tests of the rules. The
   measurements were among them, so every rule of that layer — one file, one subject, one naming — carried the exception
   of the measurements.
3. **The rule of the budget lived in the layer of the measurements.** `BudgetMachineTests` tested the logic of the budget
   — which machine a target belongs to — and it needed a database to do it, because the logic sat next to the code that
   builds an index. A rule test that reads the rules of the clock belongs with the rules.

The variables of the environment had a second problem: they were read in six files, and each of them had its own name for
one thing — the size of the corpus, the kind of the index, the size of the batch, the database to keep, the machine of the
run. Six names for one scenario, and a scenario that could only be described in prose.

## Decision

**The measurement becomes a layer of its own.**

- **The project.** `tests/Nordmann.LUI.Performance.Tests` (xUnit.net v3) holds the six files that read the clock. It is a
  project of the tree like the other three, with a row in the map of [`docs/testing.md`](../../docs/testing.md) and a layer
  in the guard of `TestLandscapeTests`.
- **The layer is the gate.** There is no attribute and no variable any more: a run of the measurement layer measures, and a
  run of the other layers does not. The lanes of the CI name the four projects they run, so a pull request proves the rules
  and does not measure — and the local loop of the developer stays fast.
- **The scenario is a file.** `tests/Nordmann.LUI.Performance.Tests/performance.scenario.json` carries the corpus, the prefix
  length, the size, the kind, the batch, the weights, the machine of the run and the database to keep. The copy in the
  repository is the default, so a run without the lane works; the lane writes the file before it measures. A member that
  nobody reads is an error and not a shrug: a file that grows a member beside the ones that are read would be a second truth
  beside the scenario.
- **The rules of the budget live in the shared infrastructure, without a test framework.** `PerformanceBudget` and
  `BudgetMachine` sit in `Nordmann.LUI.Testing` and know nothing about xUnit: the budget reports a missed target as a
  `BudgetTargetMissedException`, and the layer turns it into a red test. The unit layer references the infrastructure and
  tests the rules of the budget — `BudgetMachineTests` — without a database.
- **The guard of the whole word list is the fetch.** `tools/fetch-wordlist.sh` checks the 685,829 lines and fails loudly.
  The variable that demanded the list is gone: two guards for one fact are one too many, and the fetch is the one that
  knows the number.

## Consequences

- The unit layer names the shared infrastructure now. **Its rule stands: it opens no database.** The guard of the rule reads
  the source of the unit layer and the project file, and forbids the names that open one — `LuiCatalog`, `TestDatabase`,
  `Microsoft.Data.Sqlite` — and not the name of the infrastructure. The rule is narrowed and not lifted:
  `BudgetMachineTests` opens nothing, and the unit project has no database to open.
- A measurement that runs is a measurement of the scenario of the file. The scenario stands in the report of the run, and
  the report names the corpus: two measurements on two corpora are not a comparison.
- `PerformanceFact` and `TestGates` are gone. `FullWordListFact` moves into the shared infrastructure, because the parity
  that uses it stays in the integration layer.
- **The measurements carry the trait of the clock** (`Category=Timing`): their verdict depends on the clock, and the fast
  local loop (`--filter "Category!=Timing"`) leaves them out.
- The rule of the lane stays: a time belongs to the machine it was measured on, and a size belongs to no machine. The
  machine of a run is a member of the scenario file now — `"measurement"` is the machine of the record, `"other"` is another
  one.

## The criterion that is not met literally

Issue #122 asks: *a search for the variables of the performance run finds nothing over the repository.*

**It is not met literally, and the exception is named here instead of being argued away.** The papers of the work —
[`.agents/plan/2026-11-06-issue-64-production-readiness.md`](../plan/2026-11-06-issue-64-production-readiness.md) and
[`.agents/plan/2026-11-11-the-public-surface-and-the-measurement.md`](../plan/2026-11-11-the-public-surface-and-the-measurement.md) —
and the state file [`.agents/state/recently-completed.md`](../state/recently-completed.md) name the variables they
replaced: a paper that forgets what it replaced cannot be read as a record. **No code, no workflow, no script and no
document of the library names any of them.**

The names stand in D18 of the paper of 2026-11-11; the check that the criterion means is a search for them over the
tree outside the papers and the state file — and it finds nothing.

The criterion is lifted for the three dated records and kept for everything else. It is lifted explicitly, not argued
away: this paragraph is the lift.

## Alternatives

- **A trait instead of a layer.** Rejected: a trait is a switch in another place, and the file that reads it stays in the
  layer of the behaviour. The layer is visible in the tree — in the solution, in the map and in the report of a run.
- **The measurements stay in the integration layer, with the attribute.** Rejected: that is the state before this decision.
  The layer of the behaviour holds the rules; the measurements are numbers about the rules, and they belong beside them,
  not among them.
- **The scenario as a command-line argument.** Rejected: an argument is invisible in the report and in the diff; the file is
  both, and the file can be checked in with the default scenario.
- **The rules of the budget stay in the layer of the measurements.** Rejected: `BudgetMachineTests` would need a database for
  a rule that has none, and the unit layer is where a rule without a database belongs.

## The number of this decision

The paper of 2026-11-11 names this decision ADR 0057. The number was given to the decision of the English repository
(PR #135) before this decision was built, so this one is **0058** — the numbers are given when the decision is built, and
the paper keeps the number it was written with.
