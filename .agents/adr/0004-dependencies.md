# 0004 — The dependency set of the library, and the deviation from the closed list

> **Contract note (ADR-0056, 2026-11-12):** this record was written against a contract outside the repository — a
> private one, that no reader of LUI can know and does not have to. The repository states its own rules since: the
> rules that contract carried for LUI stand in [`AGENTS.md`](../../AGENTS.md) and in the documents of
> [`docs/`](../../docs/README.md). The dependency set itself does not change — it is still this one, and it is still
> asserted. The text below is history and stays as it was written.

> **Name note (ADR-0030, 2026-11-05):** since this record was written, `F23.StringSimilarity` is **no longer a
> dependency of the core**: the four similarity measures of ADR-0030 are LUI's own (ADR-0032, ADR-0030), and the package
> remains a test dependency — the parity tests compare against the library they replace. The runtime dependencies of
> `Nordmann.LUI` are `Microsoft.Data.Sqlite` and `symspell`. The text below is history and stays as it was written.

> **Note (ADR-0057, 2026-11-12, issue #121):** this record was written against an external contract that a reader of a
> public library cannot know. Its decision is restated in the terms of this repository: the dependency set of a
> library is closed, and a package outside the list needs an ADR that says why. The decision stands as it was
> written; the text below is history.

- **Status:** Accepted
- **Date:** 2026-11-02
- **Supersedes:** —
- **Affects:** `Directory.Packages.props`, `src/Nordmann.LUI/Nordmann.LUI.csproj`,
  `tests/Nordmann.LUI.Architecture.Tests/Repository_Layout.cs`

## Context

This repository runs a **closed dependency set**: a package that no ADR of this repository names does not land in the
project without an ADR that says why ([`AGENTS.md`](../../AGENTS.md)).

LUI needs two packages that no ADR of this repository named yet:

| Package | Licence | Why LUI needs it |
|---|---|---|
| `symspell` 6.7.3 | MIT | The typo tolerance (ADR-0012) is SymSpell over the **word beginnings** of an index, with one edit and the three best corrections. The measurements in Anhang A are SymSpell measurements. |
| `F23.StringSimilarity` 7.0.1 | MIT | The four similarity measures of ADR-0030 (Jaro-Winkler, N-gram Dice, Damerau-Levenshtein, Levenshtein). |

`Microsoft.Data.Sqlite` 10.0.14 needs no exemption: it is the provider of the persistence layer.

## Decision

**Take both dependencies, and record the deviation here.**

### Why `symspell`

SymSpell is not a "distance function with a dictionary" — it is a specific
algorithm (prefix buckets + deletes + a frequency-ranked candidate list), and
the specification's claim in ADR-0012 (typo tolerance recovers 40 % of the
misses that whole-word SymSpell cannot) is a claim *about that algorithm*.
Re-implementing it would mean re-implementing the very thing the
measurements are about, and then measuring my version against the spec's
numbers.

The price: 676,000 word beginnings per index cost about 179 MB of RAM
(Anhang A). That price is documented in the README and the dictionary is built
lazily, on the first typo-tolerant query — not at `Ensure()`.

### Why `F23.StringSimilarity`

Four measures in one small, MIT-licensed, actively maintained library, each
with a measured cost in ADR-0030 (4.5 µs for Jaro-Winkler, 12.2 µs for N-gram
Dice). Hand-rolling four measures would add four places to get subtly wrong,
for zero benefit.

### What the deviation costs

Every consumer of `Nordmann.LUI` inherits two dependencies. That is the
closed-list rule's price, paid knowingly:

- Both are MIT, single-purpose, and stable.
- Neither pulls a dependency of its own that matters.
- The dependency set is **asserted** by an architecture test, so it cannot
  grow by accident: a new `PackageReference` fails the build until an ADR
  says why.

### The alternative that was rejected

Implementing SymSpell in LUI itself. It is a few hundred lines, it removes a
dependency, and it makes LUI responsible for a non-trivial algorithm whose
correctness would have to be proven by LUI's own tests. **Rejected:** the
dependency is smaller than the risk of owning the algorithm.

## Consequences

- The dependency set of this repository is closed: `AGENTS.md` asks for an ADR for every package, and `symspell` and
  `F23.StringSimilarity` get theirs here.
  This ADR is the exemption for this project; it does **not** amend the rules of the repository. If another
  project wants them, it gets its own ADR.
- The dependency set is a public contract of the package. Changing it is a
  breaking change for consumers and needs a new ADR.

**Verified by:** `tests/Nordmann.LUI.Architecture.Tests/Repository_Layout.cs`
— `The_library_references_only_the_documented_dependencies` and
`The_library_ships_only_the_documented_runtime_dependencies`; and
`tests/Nordmann.LUI.Unit.Tests/PackageIdentityTests.cs` —
`Library_references_no_undocumented_dependency`.
