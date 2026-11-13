# 0056 — The budgets live in a file: one source, two readers

> **Note (ADR-0061, 2026-11-13, issue #125):** the specification this record was written against is deleted.
> Every section of it has a home — the ADR or the page that carries the same rule; the mapping is in
> [ADR-0061](./0061-the-specification-is-deleted-and-every-section-of-it-has-a-home.md). The sentences below
> that name the specification are history; the rule they carry lives in the ADR named beside them.

- **Status:** Accepted
- **Date:** 2026-11-12
- **Builds on:** [0020](./0020-the-performance-budgets-measured.md) (the budgets, measured),
  [0040](./0040-the-budget-table-of-the-entrance-is-a-promise.md) (the table of the entrance is a promise),
  [0041](./0041-the-budgets-of-16-run-on-a-schedule.md) (a time belongs to its machine, a size to nobody),
  [0004](./0004-dependencies.md) (the dependency set — a file format would be a dependency)
- **Affects:**
  - `tests/Nordmann.LUI.Testing/Budgets/budgets.json` (new), `tests/Nordmann.LUI.Testing/LuiBudgets.cs` (new),
    `tests/Nordmann.LUI.Testing/Nordmann.LUI.Testing.csproj` (the embedded resource)
  - `tests/Nordmann.LUI.Architecture.Tests/ReadmeBudgetsTests.cs`,
    `tests/Nordmann.LUI.Architecture.Tests/LuiBudgetsTests.cs` (new)
  - `tests/Nordmann.LUI.Testing/PerformanceBudget.cs`,
    `tests/Nordmann.LUI.Performance.Tests/BudgetPerformance.cs`,
    `tests/Nordmann.LUI.Unit.Tests/Budgets/BudgetMachineTests.cs`
  - `README.md`, `docs/benchmarks.md`, `docs/testing.md`, `AGENTS.md`,
    (ADR-0020 points at the file)
  - `tools/mutation-check.sh` (the two budget mutations edit the file now)
- **Issue:** #120 (part of #117, step 3)

## Context

The budgets of the repository were written down three times: in ADR-0020 of the specification, in the tables of the entrance
and of the record, and — as a number at the call site — in the code. The guard between them was
`ReadmeBudgetsTests`, which parsed the German table of ADR-0020 and compared it with the three tables that show the limits.
Issue #38 exists because a promise that is written down in three places drifts.

Two things made that arrangement untenable. The specification is the contract of the repository (`AGENTS.md` until now)
and it is a German document on its way out: the plan of 2026-11-11 unlinks it first (D12) and deletes it afterwards.
And the contract of the repository is changing with D14: a reader outside this repository must be able to know
everything a change needs to know — including what the budgets promise. A guard that reads a document nobody outside
the repository can know is a guard of a private contract.

The budgets are promises to the reader of the package: the table of the README is what a reader checks (ADR-0040). They
deserve a home of their own, and that home must be in the repository, in a format every tool of the tree can read.

One number was missing. The dictionary of the distance `TwoErrors` — 685,739 words of the German word list — costs
240 MB (measured 2026-11-12). The promise of 200 MB belongs to the dictionary of **one** error. The two-error
dictionary had no row of its own: it was measured, the measurement stood in the ADR of the typo distance and in the
document of the dictionary — a **miss of the old promise**, written down as one — and nothing in the tree promised it,
so nothing in the tree could keep it.

## Decision

1. **One file, one truth.** `tests/Nordmann.LUI.Testing/Budgets/budgets.json` is an embedded resource of
   `Nordmann.LUI.Testing` and the only place a limit is written down. **JSON, not YAML**: .NET has no YAML reader in
   the box, and a dependency for a file format is a package and an ADR (the plan of 2026-11-11, D6).
2. **A budget carries its promise and nothing else.** An id, the **scenario** (the corpus file, the prefix length, the
   condition), the **limit**, the **unit** and the **kind** — a time or a size (ADR-0041: a time belongs to the machine
   it was measured on, a size belongs to nobody). **The measured value does not belong in the file**: it belongs in the
   record, beside the machine it was measured on. The reader refuses a member it does not know, so a measured value in
   the file is an error and not a shrug.
   - **One budget is one limit.** A row of the tables that promises two things — the allocation (the median and the
     95th percentile), the dictionary (its size and its build) — is two budgets with one scenario: a budget carries one
     limit, one unit and one kind, and the kind of the two is not the same. The ten rows of the entrance are twelve
     budgets.
3. **Two readers.** `ReadmeBudgetsTests` compares the file with the tables of the entrance and of the record;
   `PerformanceBudget` asserts the measurements against the file. One source, two readers — and a limit that changes
   in the file, or a limit that is changed by hand in a table, fails the build.
4. **The public API of the package does not change.** `LuiBudgets` lives in a test project — `Nordmann.LUI.Testing` —
   and is not packed (ADR-0001, ADR-0018). No public type of `Nordmann.LUI` or `Nordmann.LUI.Hosting` changes: the
   page of the API (`docs/api.md`) needs no row and the package needs no migration note. This ADR is the record of the
   decision, as it is for every decision.
5. **The new row: the dictionary of two errors.** The limit is **≤ 240 MB** — the measured value — and there is **no
   time limit**: a build time is a property of the machine it was measured on, and it is measured in the record, not
   promised. The promise of 200 MB stays with the dictionary of one error. The miss of the old promise (240 MB against
   200 MB) stays written down as a miss — in the ADR of the typo distance and in the document of the dictionary — and is
   not lowered quietly anywhere.
6. **The specification points at the file.** ADR-0020 keeps its number and its §-references — they are rehung in the pull
   request that deletes the specification — but it no longer carries a number that the file carries, and no promise the
   file does not.

## What was measured

**Nothing was measured for this decision.** The limits of the file are the limits of ADR-0020, unchanged — a budget that is
lowered in passing is not a decision, it is a bug (ADR-0020, ADR-0040).

One number is new to the file, and it is not a measurement of this pull request: the **240 MB** of the dictionary of two
errors, measured on 2026-11-12 and standing in the ADR of the typo distance (ADR-0054) and in the document of the
dictionary. It is the limit, because the limit of a size is the number that was measured — and a promise of 240 MB
against a measurement of 240 MB is met, not hoped for.

## Consequences

- The budgets have one home. The specification, the tables of the entrance and of the record, and the code read it.
- **A wrong number in the file fails the build.** The two budget mutations of `tools/mutation-check.sh` edit the file
  now — the guard stays the same, the target moves — and the file is embedded, so the mutation is read by the test that
  fails.
- The file cannot grow a second truth: a member that the reader does not know is refused.
- The scenarios name the corpora of the plan (D7): the corpus files arrive with the measurement infrastructure. The
  prefix lengths are the ones of ADR-0020 today (3 000 000 entries, 1 000 000 titles, 685 739 words) and are re-decided
  against the measured corpora — never lowered in passing.
- The word count of the dictionary is **685,739** — the words of the list, not its lines. The entrance said 685,829
  before; that was the count of the lines of the fetched file.
- The public API of the package does not change: no migration note, no row on the page of the API. A reader of the
  package sees the same API.
- The reader of the repository finds the promises of the repository in the repository: the file is in the tree, the
  tables show it, and the guard holds them together.

## Alternatives

| | For | Against |
|---|---|---|
| **A — YAML instead of JSON** | the file is easier to write by hand | no reader in the box; a dependency for a file format is a package and an ADR |
| **B — the file on disk, read by path** | the file can be edited without a build | a path is a thing that is wrong once; the file travels with the assembly instead |
| **C — the limits stay in the code, the tables are checked against the code** | nothing new to learn | that is issue #38: a promise copied by hand drifts |
| **D — one file per budget** | a small file per promise | twelve files, twelve truths, twelve readers |
| **E — the measured value in the file** | the file holds the whole picture | a measurement belongs to a run and to its machine; the file is a promise (ADR-0040) |
| **F — no row for the dictionary of two errors** | the promise of 200 MB stays untouched | a promise that is measured but not written down cannot be missed, and therefore cannot be kept |
| **G — the file, and the specification keeps its table** | nothing moves in the German document | two sources of a number; the specification is deleted in a later pull request, and the number would have to move twice |
