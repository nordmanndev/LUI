# Active task

<!--
What the next session is doing. One task, in the words of the issue that
describes it. Link the issue; do not repeat the whole plan here.
-->

**Issue:** [#51](https://github.com/nordmanndev/LUI/issues/51) — PR-011: the sample, flexdatalist against the whole LUI API, a modular client (ADR-0027)
**Branch:** `feature/sample`
**State:** implemented and verified; pushed; the pull request waits for the owner's word. After it: PR-012 (the release; ask before publishing).

## Decided by the owner (2026-11-04)

- Epic #39 as recommended: #40 (merged, #45) → #41 (merged, #46) → #42 (this, one pull request) → #43 (the freeze).
  Not done: A.2, A.4 (`LuiPriority` stays), the merged sync type, `With…` methods, keyed registrations, D.3.
- There are no old databases (LUI is in development): no legacy paths, no "way out" messages.
- #35 after 0.1.0; E.1 with #30; #44 (`symspell`) after 0.1.0. The first release stays 0.1.0.
- #48: option G — the dictionary only grows, no `Stale`, no background rebuild, one dictionary per index ("jeder Index hat sein eigenes Dictionary"). ADR-0025. The freeze (#43) becomes ADR-0026.

## The next step

**Next:** PR-012 — the release (`v0.1.0`; the owner runs the tag, ask before publishing). After 0.1.0: #35, #30 (with the own
Jaro-Winkler), #31, #32, #38, #44, #20 (`Highlight` — then `fold.js`/`highlight.js` of the sample go away), the `compactLevel` follow-up of #48.
