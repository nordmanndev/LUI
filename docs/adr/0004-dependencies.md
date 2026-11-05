# 0004 — The dependency set, and the deviation from the closed library list

> **Name note (ADR-0030, 2026-11-05):** since this record was written, `F23.StringSimilarity` is **no longer a
> dependency of the core**: the four similarity measures of §11 are LUI's own (ADR-0029, ADR-0030), and the package
> remains a test dependency — the parity tests compare against the library they replace. The runtime dependencies of
> `Nordmann.LUI` are `Microsoft.Data.Sqlite` and `symspell`. The text below is history and stays as it was written.

- **Status:** Accepted
- **Date:** 2026-11-02
- **Supersedes:** —
- **Affects:** `Directory.Packages.props`, `src/Nordmann.LUI/Nordmann.LUI.csproj`,
  `tests/Nordmann.LUI.Architecture.Tests/Repository_Layout.cs`

## Context

The standards' `architecture/stack.md` runs a **closed library list**
(standards ADR-0008): a package that is not in the catalogue does not land in
a project without an ADR that says why.

LUI needs two packages that the catalogue does not name:

| Package | Licence | Why LUI needs it |
|---|---|---|
| `symspell` 6.7.3 | MIT | The typo tolerance (§10) is SymSpell over the **word beginnings** of an index, with one edit and the three best corrections. The measurements in Anhang A are SymSpell measurements. |
| `F23.StringSimilarity` 7.0.1 | MIT | The four similarity measures of §11 (Jaro-Winkler, N-gram Dice, Damerau-Levenshtein, Levenshtein). |

`Microsoft.Data.Sqlite` 10.0.14 needs no exemption: it is already in the
standards' stack as OrmLite's provider.

## Decision

**Take both dependencies, and record the deviation here.**

### Why `symspell`

SymSpell is not a "distance function with a dictionary" — it is a specific
algorithm (prefix buckets + deletes + a frequency-ranked candidate list), and
the specification's claim in §10 (typo tolerance recovers 40 % of the
misses that whole-word SymSpell cannot) is a claim *about that algorithm*.
Re-implementing it would mean re-implementing the very thing the
measurements are about, and then measuring my version against the spec's
numbers.

The price: 676,000 word beginnings per index cost about 179 MB of RAM
(Anhang A). That price is documented in the README and the dictionary is built
lazily, on the first typo-tolerant query — not at `Ensure()`.

### Why `F23.StringSimilarity`

Four measures in one small, MIT-licensed, actively maintained library, each
with a measured cost in §11 (4.5 µs for Jaro-Winkler, 12.2 µs for N-gram
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

- The standards' catalogue does not list `symspell` or `F23.StringSimilarity`.
  This ADR is the exemption for this project; it does **not** amend the
  standards. If another Nordmann project wants them, it gets its own ADR —
  or the standards adopt them.
- The dependency set is a public contract of the package. Changing it is a
  breaking change for consumers and needs a new ADR.

**Verified by:** `tests/Nordmann.LUI.Architecture.Tests/Repository_Layout.cs`
— `The_library_references_only_the_documented_dependencies` and
`The_library_ships_only_the_documented_runtime_dependencies`; and
`tests/Nordmann.LUI.Unit.Tests/PackageIdentityTests.cs` —
`Library_references_no_undocumented_dependency`.
