# Active task

<!--
What the next session is doing. One task, in the words of the issue that
describes it. Link the issue; do not repeat the whole plan here.
-->

**Issue:** [#145](https://github.com/nordmanndev/LUI/issues/145) — the creation names the route: `Create` and
`CreateLinked`. The owner's decision of 2026-11-13 on point A.3 of the review of 2026-11-04.
**Branch:** `feat/145-create-linked`, from `dev` at `9d5ca7a`.
**Decision:** [ADR-0062](../../.agents/adr/0062-the-creation-names-the-route-create-and-createlinked.md) — the route is
named in the call, as it is on the side of opening (`Index` / `LinkedIndex`).

## What is done

- **The series is complete**: steps 1 to 8 of epic #117 are merged into `dev` (PRs #126, #127, #131, #135, #136, #138,
  #139, #141), and the residue of the German is gone (PR #143, `9d5ca7a`).
- **The rename**: `LuiCatalog.Create` creates an embedded index, `LuiCatalog.CreateLinked` a linked one. The linked
  overload of `Create` is gone; the compiler does not choose the route any more. The call sites follow: the library, the
  tests, the sample, the benchmarks, the tutorial, the README, the API page.
- **The guard follows**: `The_catalog_creates_an_index_and_opens_one_that_is_there` says that `Create` is one method for
  an embedded index and `CreateLinked` one for a linked one.
- **The records carry their note**: ADR-0024 (the naming of A.3 lived in the hosting package) and ADR-0033 (the two
  overloads of `Create`) say what happened and when.

## Open

- **The pull request of #145** is open against `dev`.
- **The record of the surface** comes after it (issue #144): every point of the review of 2026-11-04, and the type or the
  member that carries it today. Its ADR is 0063 and it names `Create` / `CreateLinked`.
- **The `dev` → `main` release pull request** comes after that, and it carries its own review (`AGENTS.md`).

## The next step

The **record of the public surface** (issue #144, ADR-0063), then the **release pull request** — the owner asks for it
(`AGENTS.md`). After that the backlog of the open issues: the three guards that the work found (#128, #129, #130), the two
open bugs of the measurement (#134, #137), the documentation of the tests (#81), and the three that wait for after 0.1.0
(#31, #32, #44).
