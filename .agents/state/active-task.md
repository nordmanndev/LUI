# Active task

<!--
What the next session is doing. One task, in the words of the issue that
describes it. Link the issue; do not repeat the whole plan here.
-->

**Issue:** [#7](https://github.com/nordmanndev/LUI/issues/7) — MultiWord index
**Branch:** `feat/multi-word-index`
**State:** PR-004 is implemented and measured; waiting for the pull request and the owner's decisions.

## Where the work stands

- `MultiWord` + `Owned`: `s_compact`, the search index over `s_norm` and `s_compact`, the multi-word
  expression, N-gram Dice (own, allocation-free). ADR-0011.
- Open decisions of the owner: ADR-0010 (the §16 query budget at 3 M entries) and the proposal at the end of
  ADR-0011 (index `s_compact` only: −36 % file, −40 % build, −30 % latency).

## The next step

PR-005 — typo tolerance: the SymSpell dictionary of word beginnings (3 … 10 characters, one edit, three best
corrections), corrections only when **no** candidate stands at a word start, `CorrectedQuery` (spec §10).

**Read first:** `AGENTS.md`, `docs/spec/LUI-Spezifikation.md` §8 (the diagram), §10, §15 (the dictionary's
locking), `docs/adr/0004` (the `symspell` dependency), `docs/adr/0008`, `0011`.

**The traps:**

- The condition is "no candidate satisfies *word start*" (`Scored.WordStart < 0` for all), **not** "no hit":
  34 % of the typos have hits in the middle of other words.
- `LuiResult.CorrectedQuery` is already in the type and `null`; the builder method `TypoTolerance(maxEdits)` and the
  query option `TypoTolerance(bool)` do not exist yet.
- The dictionary is built lazily on the first typo-tolerant query (ADR-0004: 179 MB for 676,000 word beginnings) and
  extended by `Upsert`; `Remove` leaves entries. SymSpell is not thread-safe for read+write: a lock per index.
- The candidate search runs again for each of the three corrections and the candidates are united (best value per
  `doc_id`); the ranking runs with the corrected input.
