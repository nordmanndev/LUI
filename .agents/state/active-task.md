# Active task

<!--
What the next session is doing. One task, in the words of the issue that
describes it. Link the issue; do not repeat the whole plan here.
-->

**Issue:** [#38](https://github.com/nordmanndev/LUI/issues/38) — the budget table of the README as a projection: guard
the limits against §16.
**Branch:** `feature/budget-table-guard`, from `main` at `dd75649`.
**Decision:** [ADR-0040](../../.agents/adr/0040-the-budget-table-of-the-entrance-is-a-promise.md),
[ADR-0039](../../.agents/adr/0039-the-entrance-shows-the-measurement.md),
[ADR-0032](../../.agents/adr/0032-the-allocation-of-a-query.md).

## What is done

- **#61 is merged** (PR #62, merge commit `121f2bd`; the CI of the head commit is green in all four jobs). The README
  of the release shows the measurement of a fresh BenchmarkDotNet run, has an example of the bulk path, links the API
  page (`docs/api.md`, guarded by `Public_API_Documentation`), shows the matrix of the kinds and the decision guide of
  the sources, and the papers of the work live under `.agents/`.
- **#38**: the new guard `Readme_Budgets` reads the target table of §16 out of the specification and compares it with
  the budget table of the README and with the target table of the record. **It found a real drift**: the allocation
  still promised ≤ 250 / ≤ 350 KB, while the specification and the budget test have held ≤ 60 / ≤ 100 KB since
  ADR-0032. Both tables carry the limits of the contract now, and the measured values of the run of 2026-11-06
  (46 / 51 KB — bytes, so the machine does not matter). The mutation check has a mutation for it.

## Open

- **The release** (PR-012, `0.1.0`) comes after that — and the owner has to ask for it (`AGENTS.md`).
- **The specification is a dependency of the build now**: the limits of §16 are read out of it. When the owner deletes
  it at the end of the release, the limits have to live somewhere else — where is the owner's decision.
- Two points for the owner: `tools/link-check.py` is a tool, but not yet a lane of the CI; the tutorial has no guard in
  the tree (its examples are checked by hand in the review).

## The next step

The **release** — which the owner asks for. After that #35, #31, #32, #44, #59 (only on demand) and the `compactLevel`
follow-up of #48.
