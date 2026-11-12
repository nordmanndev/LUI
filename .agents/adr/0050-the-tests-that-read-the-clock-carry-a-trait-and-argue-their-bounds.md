# 0050 — The tests that read the clock carry a trait, and argue their bounds

- **Status:** Accepted
- **Date:** 2026-11-09
- **Builds on:** [0019](./0019-the-test-tree-three-layers-one-map.md) (the test tree),
  [0049](./0049-the-tests-are-sorted-by-subject-and-carry-one-suffix.md) (the names of the tests)
- **Affects:**
  - the timing tests in `tests/Nordmann.LUI.Integration.Tests/` and `samples/Lui.Sample.Tests/`
  - `tests/Nordmann.LUI.Architecture.Tests/TimingTraitTests.cs` (new)
  - `samples/Lui.Sample/Api/RequestStats.cs`, `SuggestEndpoint.cs` (a count of the queries the route began)
  - `tools/mutation-check.sh`, `docs/testing.md`
- **Issues:** #78 (epic #72), #94 (found in the review of pull request 92)

## Context

Some tests measure a time or wait for a state:
- the busy timeout;
- the dispose during a query and during a warm-up;
- the rounds of the warm-up service;
- a write beside a build;
- the read path beside a writer;
- the cost of the fold probe;
- the abandoned request of the sample.

On a loaded runner such a test can turn red with no fault. That is the most expensive kind of flake, because it costs the trust in the whole suite.

The review of pull request 92 found one: `A_request_the_page_abandons_stops_the_work_of_the_server` failed once in a full local run beside three other `dotnet` processes. The test let the client cancel after a fixed 150 ms. When the request had not reached the route by then, the server never saw an abort, and the window of ten seconds ran out.

Reproduced deterministically: a cancel after 1 ms made the test red three times in three runs, after exactly ten seconds each. A longer window would not have helped.

## Decision

**A test whose verdict depends on the clock carries `[Trait("Category", "Timing")]`**, on the test or on its class. A test depends on the clock when it **waits for a time or reads one**, in its body or in a helper of its file that it calls, at any depth. The forms are:
- `Stopwatch`, `DateTime.UtcNow`, `DateTime.Now`, `Environment.TickCount`;
- `Task.Delay`, `Thread.Sleep`;
- a `CancellationTokenSource` that cancels after a time;
- `WaitUntil`.

The first version of the guard named three forms: it measures, it waits until a deadline, it races a delay. That list left out the polling loop of #94, the very form that set this off. The review of pull request 95 (M1) found it. The rule now names the cause: a wait for a time, or a reading of one.

`TimingTraitTests` reads every test project. It resolves the helpers of a file to a fixed point, and fails for a test that reads the clock without the trait. It reads the trait by its name and value, in any spelling xUnit takes, because the filter reads the values, not the text (review of pull request 95, M2).

Two kinds of test are not timing tests:
- A wait on a signal with a timeout of seconds (`Entered.Wait(TimeSpan.FromSeconds(10)).ShouldBeTrue(…)`) only stops a hang.
- The measurements live in a layer of their own and run only on purpose (ADR-0058).

**A test asserts the order of magnitude of a time, never its milliseconds.**
- A lower bound is strict: load only makes a wait longer.
- An upper bound has room and a reason in the code. It proves seconds, not the 30 s of the provider, or a fraction of a second, not a scan.

| Test | Before | After, and why |
|---|---|---|
| `BusyTimeoutTests` (the class) | the write waits below 2.5 s / 3.5 s / 2 s | below the **effective** busy timeout plus five seconds — the catalog sets at least one second (ADR-0046), so 200 ms gives 6 s, 1 s gives 6 s, 2 s gives 7 s. The claim of #70 is seconds, not 30 s. The lower bounds stay |
| `ReadPathTests.Opening_an_index_takes_no_write_lock` | busy timeout 500 ms, the open below 500 ms | busy timeout 2 s, the open below 2 s, the write above 1.5 s. A read path that waited for the lock would wait the whole timeout |
| `FoldProbeTests.The_probe_costs_a_fraction_of_a_second` | below 500 ms | below 1 s. Measured: about 17 ms, three runs |
| `TypoDictionaryLifecycleTests.A_write_is_not_held_up_while_the_dictionary_is_being_built` | the write beats a delay of 2 s | of 10 s. The build is held until the test lets it go: a write that waited would wait for ever |
| `LuiWarmupTests` (two tests) | `WaitUntil` gives up after 10 s | after 30 s. A claim that holds returns at once |
| `CatalogTests.Dispose_waits_for_a_query_that_is_running` | the dispose is still running after 300 ms | unchanged. It is a lower bound: the query is held, so any machine is still waiting |
| `ConcurrencyTests.A_dispose_during_a_warm_up_ends_and_does_not_hang` | deadlines of 30 s | unchanged, now with the trait |
| `SampleBulkTests` (the five tests that call `Fill`) | the helper polls every 100 ms for a minute | unchanged, now with the trait: the helper waits for a time, and the guard follows the call (M1). The minute only stops a hang |
| `SampleApiTests.A_request_the_page_abandons_stops_the_work_of_the_server` | cancels after 150 ms, polls for ten seconds | **cancels once the server has the request** (a count of the queries the route began, `RequestStats.Started`), then waits for the count of aborts. The deadlines of 30 s only stop a hang |

**A test waits for the event it needs, not for a fixed window.** The sample counts the queries its route began. The count is internal: the page does not show it, and the health route does not report it.

**The fast local loop is `dotnet test Lui.sln --filter "Category!=Timing"`.** The CI runs every test, the timing tests included.

## What was measured

- **Which tests read the clock.** On `main`, the first version of the guard named the nine methods above that read the clock. The tenth, the sample test of #94, had its own polling loop, and that version did not see it (review of pull request 95, M1). The second version sees it. It also sees the five tests of `SampleBulkTests` that call `Fill`, through the helper. With the trait missing, it names all fifteen methods.
- **The trait as a filter:**
  - integration: 11 cases with `Category=Timing` (9 methods, one of them a theory with three cases), 410 without, together 421;
  - sample: 6 and 40;
  - unit: none.
- **The test of #94 under load.** The full sample project ran five times in a row beside twelve processes that spin the CPU: 46 of 46 green every time.
- **The test of #94 still finds its fault.** With the count of aborts removed from the route, the test is red after its thirty seconds with "the server ended the request it no longer has to answer — within thirty seconds".
- **The guard bites.** The mutations `a-clock-test-without-the-trait` (a new test with a `Stopwatch`) and `a-clock-class-without-the-trait` (the trait of `BusyTimeoutTests` removed) are caught.

## Consequences

- A new test that measures, polls or races a delay needs the trait, or the build is red. The trait is a filter, not an excuse: such a test still has to hold on a loaded runner, with its bounds argued.
- The sample has one more internal count. It costs one interlocked increment per query and is not part of its API.
- The bounds are wider, so a regression of a few hundred milliseconds no longer fails a test. That is on purpose: the measurements (`docs/benchmarks.md`) are where milliseconds are judged.

## Alternatives

- **A custom trait attribute (`[Timing]`).** Rejected: xUnit v2 needs a discoverer for it. The plain `[Trait]` is what the filter reads, by name and value, and the guard reads it the same way.
- **Running the timing tests in a lane of their own.** Rejected: they are a dozen cases. A lane would cost a runner and a setup for seconds of tests. The trait already gives the developer the fast loop.
- **A longer window for the sample test.** Rejected: measured, the window was not the cause. A cancel before the server has the request fails after any window.

## Note (2026-11-12, issue #122)

[ADR-0058](./0058-the-measurement-is-a-layer-of-its-own.md) gives the measurement a layer of its own: the measurements of
the tree are in `tests/Nordmann.LUI.Performance.Tests`, and the gate of the measurements is the layer. The decision stands; the sentences above that named a variable of the environment are rewritten, because that variable died with ADR-0058.
