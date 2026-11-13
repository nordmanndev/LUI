# Active task

<!--
What the next session is doing. One task, in the words of the issue that
describes it. Link the issue; do not repeat the whole plan here.
-->

**Issue:** [#125](https://github.com/nordmanndev/LUI/issues/125) — the specification is deleted, and every one of its
sections is rehung onto the ADR or the page that carries the same rule. Step 8 of epic #117, the last of the series.
**Branch:** `feat/125-specification-goes`, from `dev` at `46f7c36`.
**Decision:** [ADR-0061](../../.agents/adr/0061-the-specification-is-deleted-and-every-section-of-it-has-a-home.md) —
the mapping table of every section of the deleted document.

## What is done

- **Steps 1 to 7 of the series are merged** — PRs #126, #127, #131, #135, #136, #138 and #139 (the last one as
  `46f7c36`, its CI green in all eleven jobs). `dev` is 52 commits ahead of `main`, and `main` stays the frozen release
  branch of `v0.1.0-alpha`.
- **The sweep is committed** (`552b255`): 188 files, one mapping, one pass. Every `§x` of the specification became the
  ADR or the page that carries the same rule. A reference that names the document it belongs to was left alone —
  `docs/normalization.md` has a section 7 of its own, and the plan has sections of its own.
- **The papers carry their note**: 31 records that were written against the specification say so, with a dated note
  that says where the rule lives now. The records are history; the pointer is rehung.
- **The guard**: `SpecificationIsGoneTests` — the file is gone, the tree around the papers names it nowhere, and every
  section reference names a document that exists.

## Open

- **The pull request of #125** is open against `dev`; the acceptance criteria of the issue are the three facts of the
  guard, and the five test projects and the build are green.
- **The `dev` → `main` release pull request** comes after that, and it carries its own review (`AGENTS.md`).

## The next step

The **release pull request** — the owner asks for it (`AGENTS.md`). After that the backlog of the open issues: the
three guards that the work found (#128, #129, #130), the two open bugs of the measurement (#134, #137), and the three
that wait for after 0.1.0 (#31, #32, #44).
