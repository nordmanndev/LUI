# 0007 — The catalog checks the SQLite it runs on

- **Status:** Accepted
- **Date:** 2026-11-02
- **Supersedes:** —
- **Affects:** `src/Nordmann.LUI/LuiCatalog.cs`, `src/Nordmann.LUI/Normalization/FoldProbe.cs`,
  `src/Nordmann.LUI/Normalization/TokenizerFold.cs`, `src/Nordmann.LUI/Indexing/IndexDefinition.cs`

## Context

`docs/normalization.md` §7 left one decision open for this pull request. The
fold table of the C# `Normalizer` was measured from the SQLite that
`Microsoft.Data.Sqlite` bundles (3.53.3). An application can load another
SQLite, and one that folds text differently makes an index stored with the old
folding invisible to its queries — silently, which is the worst way to fail.

## Decision

**`LuiCatalog.Open` asks the SQLite that is loaded, and refuses to run on one
that disagrees with the table.** Three checks, in this order:

1. **The version.** `sqlite_version()` must be 3.45 or newer (the trigram
   tokenizer's `remove_diacritics`, §17.3). Older: `LuiEnvironmentException`
   with both versions.
2. **The folding** (`FoldProbe`). An in-memory FTS5 table with the trigram
   tokenizer is filled with one text per code point (`xx<c>xx`), and the
   trigrams are read back through `fts5vocab` and stitched into the folded
   text. The probe covers every code point the table lists, the ranges in
   which tokenizers fold (ASCII, Latin-1, Latin Extended-A/B, combining
   marks, Greek, Cyrillic, Armenian, Latin Extended Additional, Greek
   Extended, letterlike symbols, full-width forms) **in full** — so a mapping
   the table forgot is found, not only a mapping it got wrong — and a spread
   of 96 code points over the rest of the plane, which must come out
   unchanged. The verdict is cached for the process: the native library is
   the same for every connection.
3. **The definition of every index carries the identity of the table**
   (`foldTable`, a hash of the table's entries — not of the file, so line
   endings do not change it). `Ensure()` compares the stored definition with
   the requested one; an index stored with another table fails with
   `LuiDefinitionMismatchException`, which names both. The answer is `Drop()`
   and ensure again — an index is derived data.

**An index is never rebuilt silently** (§7.1). `Ensure()` compares *everything*
that decides what the tables hold: kind, source, key type, the German
transliteration, whether there is a payload, the fold table. The similarity
measure is not part of it: it is a default of the query and changes no table.

**The definition is JSON, written by a source-generated serialiser.** No
reflection (the plan's acceptance bar), and `lui_meta` stays readable by a
human with `sqlite3`.

## Consequences

- The probe costs a fraction of a second, once per process (measured in
  `docs/benchmarks.md`). The 5 ms-per-`Open` alternative — a handful of code
  points — would have missed exactly the case that matters: a newer SQLite
  that folds a character the table does not know.
- A consumer that deliberately loads another SQLite gets a clear error with the
  differing code points and the way forward (use the bundled one, or
  regenerate the table with `tools/FoldTableGenerator` and build the indexes
  again). The table stays a maintenance obligation; the probe makes it a
  loud one.
- `Rebuild()` (PR-007) is the gentler answer for the mismatch of the fold
  table; until it exists the answer is `Drop()` and `Ensure()`.
