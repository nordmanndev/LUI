# 0003 — The nine open questions of the specification are deferred

- **Status:** Accepted
- **Date:** 2026-11-02
- **Supersedes:** —
- **Affects:** `docs/spec/LUI-Spezifikation.md` §21, the public API surface

## Context

The specification ends with nine open questions (§21). Each of them describes
a *feature* that is plausible and, in some cases, cheap — and each of them
changes the public API, the schema, or the sample.

Shipping an open question as an API means shipping a promise nobody has
measured yet.

## Decision

**All nine are deferred from 1.0.** Each one gets a line here with the
condition that would revive it, so the next decision costs a page instead of
an archaeology.

| # | Question | Deferral | What would revive it |
|---|---|---|---|
| O1 | Do 1–2 characters find *every* word in a `MultiWord` index (`ap` → „Gedeckter **Ap**felkuchen")? | Deferred | A user complaint that the two-letter path only finds text beginnings. It needs a table of word beginnings per entry — a schema change, so it deserves its own design. |
| O2 | `VIRTUAL` instead of `STORED` for the normalised columns — saves space, costs on `rebuild` | Deferred | A measured size problem: the file grows beyond what the deployment can hold. Then it is a measurement question, and the measurement is cheap to take. |
| O3 | Do capitals count as a word beginning (`cupfin` → `#WorldCupFinal`)? | Deferred | A `SingleWord` index over hashtags that misses them. It becomes an option of the index kind, not of the query. |
| O4 | Does `Linked` need a weight *expression* (`clicks + 10 * favorites`) rather than a column? | Deferred | A consumer whose weight is computed. The escape hatch exists already: a generated column on the application's side. |
| O5 | Is "German transliteration on/off" enough for multiple languages? | Deferred | A consumer with a corpus the German rules mangle. The flag exists (`.GermanTransliteration(false)`); a general rule set is a bigger change. |
| O6 | Filters on `Linked` (`published = 1` only) | Deferred | A consumer with rows that must not be searchable. The implementation is a `WHERE` clause in the triggers — but a filter that is wrong hides entries silently, so it deserves its own tests first. |
| O7 | Blend word-start and weight softly instead of in stages | Deferred | Real click data. The staged ranking (§9.2) is what the measurements in Anhang A actually support; a soft blend decided without data would be a guess dressed as a feature. |
| O8 | Should an empty input suggest the `System`-priority entries ("what do you suggest?")? | Deferred | A product decision, not a technical one — it belongs to whoever owns the search field. The mechanism (a query without a term) exists. |
| O9 | Are four priority classes enough? | Deferred | A consumer that needs a fifth. More classes cost bits of the weight field in the `rowid`, so the change is not free — and four covers system, high, elevated, normal. |

## Consequences

- The 1.0 API is exactly the decided surface of the specification. Nothing in
  the README is aspirational.
- Each deferred question carries its own revival condition, so "we might want
  that someday" turns into "here is what would have to be true".
- A question that turns out to be urgent is not blocked: it gets its own ADR
  that supersedes this one.

**Verified by:** the architecture test
`Public_API_Shape.The_public_api_is_synchronous` and the API surface the
package smoke test exercises — neither knows anything about O1–O9, because
none of them is in the API.
