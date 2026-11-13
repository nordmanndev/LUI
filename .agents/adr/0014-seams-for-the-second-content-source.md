# 0014 — Seams for the second content source: the kind and the source as strategies, one direction inside

> **Name note (ADR-0023, 2026-11-04):** since this record was written, `Owned` is called `Embedded`: `ILuiOwnedIndex` is
> `ILuiEmbeddedIndex`, `LuiIndexBuilder` is `LuiEmbeddedIndexBuilder`, and `OwnedIndex`, `OwnedSource`, `OwnedSync` and
> `OwnedCheck` are `Embedded…`. The text below is history and stays as it was written.

- **Status:** Accepted
- **Date:** 2026-11-03
- **Supersedes:** —
- **Affects:** `src/Nordmann.LUI/` (internal only), `tests/Nordmann.LUI.Architecture.Tests/Internal_Layering.cs`,
  ADR-0014, ADR-0001 (the namespace layout)

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
expression. `PreparedQuery` holds the forms of the input and asks the rules; a content source has them as `Rules`.
Nothing else asks which kind an index is: an architecture test fails on the name of a kind (`SingleWord`, `MultiWord`,
with or without `LuiIndexKind.`) outside the rules, the builder that names the kind, and the enum itself; another one
fails when anything but `IndexKindRules` knows the classes of the rules — `rules is MultiWordRules` is the same
comparison under another name (review of pull request 14, S2).

**The content source is a strategy** (D2). `Schema/ContentSource` carries what depends on where the content lives: the
table names, the statements that create the index, the prefix, candidate and hit statements, the count, and the
statement the typo dictionary is built from. Each statement keeps the columns the query path reads in fixed places
(`(rowid, text)`, `(doc_id, key, payload …)`, `(text, weight)`), whatever the source calls them. `OwnedSource` is its one
implementation today and holds what `OwnedSchema` and `OwnedSql` held. The payload is read by a `PayloadReader` the index
gives the query path (`SerializedPayload` for `Owned`; columns for `Linked`). The catalog builds a typo dictionary from
a function it is given, not from a table name. `LuiCatalog.DropIndex` asks `ContentSource.DropStatements`.

**One direction inside** (D3), pinned by NetArchTest — between the internal namespaces, and through the root: no
layer may depend on the builders (that would be a way up past the rule), and the catalog is the one named exception, for
`Querying` and `Indexes`, which need its operation scope and its connections until it is taken apart (D4, PR-007).
What writes and needs connections — `Sync`, `Check`, `Rebuild` (PR-007) — runs in `Indexes`; `Schema` gives the SQL
for it (review of pull request 14, S1, S4):

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
| Query, SingleWord, 200,000 entries, five runs each, alternating with `main` | p50 0.51 ms (main 0.53), p95 1.21 ms (main 1.31) — the ranges overlap; alloc p50 63 KB / p95 244–247 KB, the same as `main` |
| Query, MultiWord, 200,000 entries, five runs each, alternating with `main` | p50 1.05 ms (main 1.06), p95 2.69 ms (main 2.72) — the ranges overlap; alloc p50 51 KB / p95 271 KB, **identical** to `main` |

`main` was measured in a worktree of `55be41a` with the same word list. **A first comparison was wrong**: a worktree has
no `artifacts/`, so `QueryPerformance` fell back to the sample list of the repository and measured another corpus — it
showed a multi-word allocation of 82 KB on `main` against 51 KB here, and a single-word p95 twice as high, neither of
which exists (review of pull request 14, N5). `QueryPerformance` now reports the corpus it measures.

## Consequences

- PR-006 adds `LinkedSource : ContentSource`, a column `PayloadReader` and `LinkedIndex : ILuiIndex` in `Indexes`; the
  query path does not change. `ContentSource.DropStatements` learns the stored source then.
- A rule of a kind has one place; a third kind would be a third rules class and a case in `IndexKindRules.For`.
- For PR-006, from the review of pull request 14: the hits SQL and its `PayloadReader` come from the source together
  (S3); `DropStatements` decides by the stored source (N2); `CompactColumn` gives the column definition, not a
  fragment with its comma (N3); a `long` key near 2³²−1 through `HitReader` (N6).
- Not done here: the catalog keeps its five responsibilities (D4) and the builder still runs its DDL through the
  catalog (D6) — both with PR-007, where `Check`/`Rebuild`/`Sync` need the seams.

**Verified by:** `tests/Nordmann.LUI.Architecture.Tests/Internal_Layering.cs` —
`A_namespace_knows_only_the_ones_below_it` (five layers), `No_layer_goes_up_through_the_facade` (six layers),
`Only_the_rules_know_the_rules_of_a_kind`, `Only_the_rules_of_a_kind_ask_which_kind_an_index_is` — each checked with a
deliberate violation: a `Schema` type that references `Querying` and compares a kind; a `Schema` type that builds an
index through the builder; `source.Rules is MultiWordRules`; `using static LuiIndexKind` with `kind == MultiWord`. And
the unchanged test suite.
