# 0014 — Seams for the second content source: the kind and the source as strategies, one direction inside

- **Status:** Accepted
- **Date:** 2026-11-03
- **Supersedes:** —
- **Affects:** `src/Nordmann.LUI/` (internal only), `tests/Nordmann.LUI.Architecture.Tests/Internal_Layering.cs`,
  `docs/spec/LUI-Spezifikation.md` §17.2, ADR-0001 (the namespace layout)

## Context

`Linked` (PR-006) is the second content source: the content lives in a table of the application, with other names for
the table and the columns, a payload read from columns and no write methods. The DevEx review of issue #11 (D1, D2, D3,
D5) found the code wired to one source and one `if` per kind:

- the kind was asked in four files (`OwnedSchema` twice, `PreparedQuery` three times, `Similarity.DefaultFor`) — the
  M1 of the review of pull request 8, a prefix path that was compact in one place and not in the other, was exactly a
  forgotten place;
- `QueryRunner` built `lui_<name>` and its columns into its SQL; the typo dictionary read `lui_<name>` itself;
- `Indexing` and `Querying` referenced each other (`OwnedIndex` → `QueryRunner` → `OwnedSchema`);
- `QueryRunner` had grown to 497 lines: candidates, correction, union, hits, timings.

Without a seam `Linked` gets a copy of the query path or an `if (linked)` in every method. With the seam drawn
**before** the feature, the refactoring can be reviewed on its own, against unchanged tests.

## Decision

**The kind is a strategy** (D1). `Schema/IndexKindRules` with `SingleWordRules` and `MultiWordRules` holds everything
that follows from the kind: the indexed column, the source of the prefix columns, the compact column, the storage of
`s_norm`, the default similarity, the words of an input, the prefix key, when the prefix path answers, and the FTS5
expression. `PreparedQuery` holds the forms of the input and asks the rules. Nothing else asks which kind an index is:
an architecture test fails on `LuiIndexKind.SingleWord`/`MultiWord` outside the rules, the builder that names the kind,
and the enum itself.

**The content source is a strategy** (D2). `Schema/ContentSource` carries what depends on where the content lives: the
table names, the statements that create the index, the prefix, candidate and hit statements, the count, and the
statement the typo dictionary is built from. Each statement keeps the columns the query path reads in fixed places
(`(rowid, text)`, `(doc_id, key, payload …)`, `(text, weight)`), whatever the source calls them. `OwnedSource` is its one
implementation today and holds what `OwnedSchema` and `OwnedSql` held. The payload is read by a `PayloadReader` the index
gives the query path (`SerializedPayload` for `Owned`; columns for `Linked`). The catalog builds a typo dictionary from
a function it is given, not from a table name. `LuiCatalog.DropIndex` asks `ContentSource.DropStatements`.

**One direction inside** (D3), pinned by NetArchTest:

```text
Nordmann.LUI            the public types and the facade (LuiCatalog, the builders) — outside the rule
  Indexes               OwnedIndex (and LinkedIndex): puts a source, the writes and the query path together
    Querying  Writing   the query path · the write statements
      Schema            definitions, RowId, KeyCodec, the rules of a kind, the content sources and their SQL
        Typos  Normalization
```

`Indexing` is gone: its parts moved to `Schema` (`Identifier`, `IndexDefinition`, `KeyCodec`, `RowId`, `Setting`, the
schema), `Writing` (`WriteStatements`) and `Indexes` (`OwnedIndex`, `TypoSource`).

**The query is a pipeline** (D5). `QueryRunner` (110 lines) runs the stages in one snapshot: `CandidateReader` (the
candidates of one input and their word start), `TypoCorrection` (whether it is wanted, the corrections, the union),
`Ranker`, `HitReader` (key and payload of the hits). The code of each stage is the code it was, moved.

## What was measured

Nothing of the behaviour may move, so it was measured, not assumed:

| Check | Result |
|---|---|
| The schema of an index | `sqlite_master` of a single-word and a multi-word index, created by `main` and by this branch: **byte-identical** (128 lines) |
| Tests | the golden, query-plan, quality, typo and concurrency tests pass with their assertions unchanged; the three call sites of internals in the tests (`QueryPlanTests`, `SimilarityTests`) name the new types |
| Query, SingleWord, 200,000 entries | p95 0.95–1.23 ms per input length (before: 0.78–1.22 ms), alloc p50 63 KB / p95 244 KB (before: the same) |
| Query, MultiWord, 200,000 entries, five runs each, alternating | p50 1.05 ms (main 1.07), p95 2.79 ms (main 2.70) — the ranges overlap; alloc p95 271 KB (main 272); alloc **p50 51 KB (main 82)** in every run |

The lower median allocation of the multi-word query is measured, not explained: the code of the stages is unchanged,
so it is most likely the JIT (inlining, and with it the stack allocation of .NET 10) treating the smaller methods
differently. It is not claimed as an improvement.

## Consequences

- PR-006 adds `LinkedSource : ContentSource`, a column `PayloadReader` and `LinkedIndex : ILuiIndex` in `Indexes`; the
  query path does not change. `ContentSource.DropStatements` learns the stored source then.
- A rule of a kind has one place; a third kind would be a third rules class and a case in `IndexKindRules.For`.
- Not done here: the catalog keeps its five responsibilities (D4) and the builder still runs its DDL through the
  catalog (D6) — both with PR-007, where `Check`/`Rebuild`/`Sync` need the seams.

**Verified by:** `tests/Nordmann.LUI.Architecture.Tests/Internal_Layering.cs`
(`A_namespace_knows_only_the_ones_below_it`, five layers; `Only_the_rules_of_a_kind_ask_which_kind_an_index_is`) —
both checked with a deliberate violation (a `Schema` type that references `Querying` and compares a kind: two
failures) — and the unchanged test suite.
