---
name: Task
about: One unit of work — code or docs — produces exactly one PR
title: "[TASK] <short imperative>"
labels: ["type:task", "status:queued"]
---

> One issue, one PR. The issue body is the contract; the acceptance criteria
> are what the PR is checked against.

## Context

<!--
Why is this task needed now? Link the parent issue. Name the decisions
(ADRs) it implements, and the ones it touches.
-->

**Parent issue:** #<number> (or "none — standalone task")

**ADRs:** ADR-<number>, ADR-<number> (or "none")

**Affected area(s):**
- [ ] `src/Nordmann.LUI/`
- [ ] `tests/`
- [ ] `benchmarks/`
- [ ] `samples/Lui.Sample/`
- [ ] `docs/`
- [ ] CI / build wiring

## Acceptance criteria

<!--
Concrete and checkable. "The query works" is not a criterion;
"a query for 'apfelkuch' returns 'Apfelkuchen' first" is.
-->

- [ ] <criterion>
- [ ] <criterion>

## Verification

<!--
What proves it. Name the test, the measurement, the command. "I ran the tests"
is not a verification — the output is.
-->

- [ ] `dotnet test` — <expected result>
- [ ] `dotnet pack` + `tools/package-smoke-test.sh` — <expected result>
- [ ] <measurement, if the task touches performance>

## Notes

<!--
Anything the implementer needs to know and cannot derive from the repository:
the trap, the edge case, the decision that is already made.
-->
