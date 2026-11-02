# Active task

<!--
What the next session is doing. One task, in the words of the issue that
describes it. Link the issue; do not repeat the whole plan here.
-->

**Issue:** —
**Branch:** —
**State:** the repository foundation (PR-001) is in progress.

## Where the work stands

- The repository is scaffolded: solution, props, CI, tests, docs, ADRs.
- The library itself is **not** implemented yet — it arrives with PR-002 and
  onwards, following `docs/plan/2026-11-02-lui-repo-plan.md`.

## The next step

PR-002 — the normaliser, and the SQL ↔ C# parity test over the word corpus.

**Read first:** `AGENTS.md`, `docs/spec/LUI-Spezifikation.md` §6 and §19,
`docs/domain/lui.md` §3.

**The trap:** the normalisation rule exists twice — as SQL expressions and as
a C# `Normalizer`. The C# side has to match what FTS5's trigram tokeniser
actually does, not what the specification says it does. Measure first
(`tests/Nordmann.LUI.TestData/wordlist/edge-cases.txt` holds the cases whose
behaviour is still open), then write the expectation.
