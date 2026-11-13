---
name: Bug
about: Something behaves differently from the contract
title: "[BUG] <what is wrong>"
labels: ["type:bug", "status:queued"]
---

> A bug is a difference between the code and the **contract**. The contract is
> the set of the [ADRs](../../.agents/adr/README.md) (ADR-0061). If a decision is
> the wrong one, that is not a bug — that is a new ADR.

## What happens

<!--
The observed behaviour. Concrete: the input, the output, the error.
-->

## What the contract says

<!--
The decision and the sentence. Quote it.
-->

**ADR:** ADR-<number>

## Reproduction

<!--
The smallest thing that shows it: the test, the query, the call.
-->

```csharp
// or SQL, or a shell command
```

## Notes

<!--
Suspected cause, if you have one. A wrong suspicion is still useful — it
saves the next person a day.
-->
