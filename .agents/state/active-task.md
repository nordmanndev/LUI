# Active task

<!--
What the next session is doing. One task, in the words of the issue that
describes it. Link the issue; do not repeat the whole plan here.
-->

**Issue:** [#38](https://github.com/nordmanndev/LUI/issues/38) — the budget table of the README, guarded against §16.
**Branch:** to be cut from `main` at `121f2bd` (the merge of PR #62).
**Decision:** [ADR-0039](../../.agents/adr/0039-the-entrance-shows-the-measurement.md),
[ADR-0038](../../.agents/adr/0038-the-public-api-has-a-page-of-its-own.md),
[ADR-0037](../../.agents/adr/0037-the-papers-of-the-work-live-under-agents.md),
[ADR-0036](../../.agents/adr/0036-the-readme-the-specification-and-the-tutorial.md) (decisions 1 and 4 superseded),
[ADR-0022](../../.agents/adr/0022-the-benchmark-results-in-the-readme.md) (restated).

## What is done

- **#61 is merged** (PR #62, merge commit `121f2bd`; the CI of the head commit is green in all four jobs). The
  README of the release:
  - the **measurement** of a fresh BenchmarkDotNet run stands in the README, and the guard holds it against the
    committed reports;
  - the **bulk path** has an example (`Sync`, `BatchSize`, the report) — a C# block the package smoke test executes;
  - the **public API** has a page of its own (`docs/api.md`, 38 types of the core and one of the hosting), and a test
    holds it up (`Public_API_Documentation`);
  - the **kinds** are a matrix (`SingleWord`/`MultiWord`, with the typo tolerance on and off), the **sources** a
    decision guide, and the **sample** moves (`docs/images/sample.gif`);
  - the **papers of the work** — the ADRs, the plans, the specification — live under `.agents/`, with an index of their
    own; neither the README nor `docs/` links them any more.
- The **tutorial** (`docs/tutorial/`) is the way: from an empty database to a working autocomplete, in five steps.
- The **map of the documentation** (`docs/README.md`) and the **topic** column over the ADRs.

## Open

- **#38** — the budget table of the README is guarded against §16: a test that reads the targets of the specification
  and fails when the table of the README does not match them.
- The **release** (PR-012, `0.1.0`) comes after that — and the owner has to ask for it (`AGENTS.md`).
- Two points for the owner: `tools/link-check.py` is a tool, but not yet a lane of the CI; the tutorial has no guard in
  the tree (its examples are checked by hand in the review).

## The next step

**#38** first, then the release — which the owner asks for. After that #35, #31, #32, #44, #59 (only on demand) and
the `compactLevel` follow-up of #48.
