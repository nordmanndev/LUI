# 0051 — xUnit.net v3, run by VSTest

- **Status:** Accepted
- **Date:** 2026-11-09
- **Builds on:** [0019](./0019-the-test-tree-three-layers-one-map.md) (the test tree),
  [0047](./0047-the-pipelines-change-only-with-the-repository.md) (the pipelines),
  [0050](./0050-the-tests-that-read-the-clock-carry-a-trait-and-argue-their-bounds.md) (the timing tests)
- **Affects:**
  - `Directory.Packages.props`: `xunit` 2.9.3 → `xunit.v3` 4.0.2; `xunit.runner.visualstudio` 3.1.5 → 4.0.1
  - the four test projects: `OutputType` `Exe`, `IsTestingPlatformApplication` `false`
  - `.editorconfig` (xUnit1051 in the tests)
  - `SampleHost`, the two gate attributes, `TestGates` (new)
  - the waits of the tests (the token of the test)
  - `docs/testing.md`
- **Issue:** #79 (epic #72). The owner's decision of 2026-11-09: the CI stays on VSTest for now.

## Context

The tests ran on `xunit` 2.9.3, a line in maintenance. v3 brings three things the tests imitated:
- a skip decided when the test runs: the gate attributes set `Skip` in their constructor, when the tests were found;
- the cancellation token of the test (`TestContext.Current.CancellationToken`);
- the test project as an executable, on Microsoft.Testing.Platform (MTP).

`xunit.v3` 4.0.2 (2026-10-09; 4.0.3 is from today, too new to take) brings MTP 2.5.1 with it. On the .NET 10 SDK, MTP refuses the VSTest target of `dotnet test` as long as a project says it is an MTP application:

```text
error : Testing with VSTest target is no longer supported by Microsoft.Testing.Platform on .NET 10 SDK and later.
```

Moving the CI to MTP would change a lot at once:
- `global.json` (the test runner);
- every test call of the workflows: other options for the filter, the hang dump and the TRX;
- the workflow guard (`HangFlags`);
- the mutation check, which reads the output of VSTest;
- two more test packages (HangDump, TrxReport).

## Decision

**xUnit.net v3 4.0.2, run by VSTest through `xunit.runner.visualstudio` 4.0.1.**
- Every test project is an executable (`OutputType` `Exe`, which v3 requires).
- Every test project says `IsTestingPlatformApplication` `false`. The xUnit package sets it to `true` only when it is empty, so the project's value wins, and `dotnet test` runs the project through VSTest as before.
- The CI, the filters, the hang dumps, the TRX and the mutation check do not change.
- Moving to MTP is a step of its own, when there is a reason for it.

**The gates are dynamic skips.**
- `[PerformanceFact]` and `[FullWordListFact]` set `Skip` together with `SkipType = typeof(TestGates)` and `SkipUnless = nameof(…)`.
- v3 asks `TestGates.MeasurementsRun` and `TestGates.FullWordListRuns` when the test is about to run. The reason stands in the report.
- The rule of ADR-0019 holds:
  - with `LUI_REQUIRE_FULL_WORDLIST=1` and no list, the parity runs and fails with the reason;
  - without the variable and without the list, it is skipped and says why.
- The attributes pass the source position to the base (`[CallerFilePath]`, `[CallerLineNumber]`; analyzer xUnit3003).

**The shapes of v3.**
- `ITestOutputHelper` lives in `Xunit`; `Xunit.Abstractions` is gone.
- `IAsyncLifetime` returns `ValueTask` and is an `IAsyncDisposable`. v3 disposes a fixture once, through `DisposeAsync`. So `SampleHost` now deletes its database file there, not in a `Dispose` that would no longer be called.
- `NetArchTest.Rules.TestResult` and `Xunit.TestResult` share a name. `PublicApiShapeTests` names the first by an alias.

**The token of the test goes where a test waits.**
- Every `Task.Delay` and every `WaitAsync` of the tests passes `TestContext.Current.CancellationToken`. That is where a cancelled run would otherwise hang.
- The analyzer xUnit1051 asks for the token at every call that takes one: about 475 calls of the library, whose token is optional. Most of those tests are not about cancellation, and the token would be noise in all of them. The rule is set to `none` for the tests in `.editorconfig`, with this reason beside it.

## What was measured

| | before (`main`, `1ecc362`) | after |
|---|---:|---:|
| Unit | 263 passed | 263 passed |
| Integration | 412 passed, 9 skipped (421) | 412 passed, 9 skipped (421) |
| Architecture | 94 | 94 |
| Sample | 46 | 46 |

- **The same tests.** The names, listed before and after, are the same in every project. Three lines differ only in the display of the arguments of a theory, which v3 shortens at another length (`"s_compact : (\"near\" AND \"apfel\" AND \"kuchen\""···` against the whole string).
- **The gates, at run time:**
  - without `LUI_PERF` the measurement is skipped, and the report says `Measurement: set LUI_PERF=1 …`;
  - with `LUI_PERF=1 LUI_PERF_ENTRIES=2000` it runs and passes;
  - `LUI_WORDLIST=/nonexistent/…` alone skips the parity with `The full word list is not present (…)`;
  - with `LUI_REQUIRE_FULL_WORDLIST=1` added, the parity fails.
- **The hang dump still reaches the test.** v3 runs the test in a process of its own. A probe test that sleeps for ever, run with the flags of the CI (`--blame-hang-timeout 20s --blame-hang-dump-type full`), aborted with "Collecting hang dumps from testhost and its child processes". It left two dumps: one of `dotnet`, the test host, and one of `Nordmann.LUI.Unit.Tests`, the process where the test hung.

## Consequences

- A gate is a property of `TestGates`. A new gate is a new property, read when the test runs.
- The test projects can also be started on their own (`dotnet run --project tests/Nordmann.LUI.Unit.Tests`). The CI does not do that.
- `xunit.v3` brings MTP 2.5.1 with it, including its telemetry extension, as a dependency of the tests only. Nothing of it reaches the packages (`RepositoryLayoutTests`).
- Dependabot's group `xunit*` covers `xunit.v3`.

## Alternatives

- **MTP now** (the issue's "the CI on the new runner"). Not now, by the owner's decision. The cost is in the context above, and it would take several CI rounds. It is a step of its own.
- **`Assert.Skip` in the body of every gated test.** Rejected: the gate belongs to the attribute that names it. The body of a measurement would carry a line that is not the measurement.
- **The token of the test in every call (xUnit1051 as an error).** Rejected: about 475 tokens in tests that are not about cancellation. The token goes where a test waits.
