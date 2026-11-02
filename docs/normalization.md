# The normalisation rule

> LUI's normalisation rule exists **twice**: as SQL expressions in the index
> columns and as the C# `Normalizer`. This document says what the rule is,
> what was *measured* about the part that lives inside SQLite, and how the
> two sides are held together.
>
> The specification (LUI-Spezifikation §6.2) demands the parity; this
> document is the evidence.

---

## 1. The rule

```text
text  ──► German transliteration ──► fold (case + diacritics) ──► search form
                                    └──► separators removed ──────► compact form
```

| Step | Where it runs | What it does |
|---|---|---|
| German transliteration | **both** sides | `ä → ae`, `ö → oe`, `ü → ue`, `ß → ss` (and the capital letters) |
| Fold (case, diacritics) | **SQLite's tokenizer**, on the indexed text *and* on the query | `Ä → a`, `Café → cafe`, `İ → i` |
| Separators removed | **both** sides | the compact form: `Apfel Kuchen → apfelkuchen` |

**The order matters.** The transliteration has to come *before* the folding,
because the folding removes the umlaut: `Ä` folded first becomes `a`, and the
transliteration has nothing left to do. Transliterated first, `Ä` becomes
`Ae`, and the folding makes it `ae` — the same result the query produces, so
the two sides find each other.

### What the two sides store

| | Content |
|---|---|
| The index column (`s_norm`) | the transliterated text — case and diacritics **untouched** |
| The search index (FTS5) | the folded text — the tokenizer folds the column when it indexes it |
| The C# search form | the folded text — the same, computed in C# |

The column is not folded because SQLite cannot fold it: the removal of the
diacritics lives in the tokenizer, and a generated column cannot call a
function that the tokenizer provides. The tokenizer applies its folding to
the indexed text *and* to the query, so both sides of the comparison see the
same thing.

---

## 2. What the tokenizer actually does (measured, not assumed)

Everything below was measured against FTS5's trigram tokenizer with
`remove_diacritics 1` in SQLite 3.53.3 — the version `Microsoft.Data.Sqlite`
10.0.14 ships. The measurement is in `src/Nordmann.LUI/Normalization/tokenizer-fold.txt`
and is re-measured by the parity test on every build.

### The folding is per character

The tokenizer folds each character on its own. It does **not** normalise the
text first: `e` + combining acute becomes `ecole` because the combining mark
is dropped, not because the text was composed to `é` first. That difference
matters for sequences like `e` + acute + cedilla, where normalising would
give `ǽ` and the per-character folding gives `e` + cedilla.

### What it folds

| | |
|---|---|
| Case | ASCII, Latin-1, Latin Extended-A and B, Greek, Cyrillic |
| Diacritics | the Latin-1 and Latin Extended range — `é → e`, `ü → u`, `å → a`, `ǰ → j` |
| Characters dropped | 25 combining marks (U+0300 … U+0331, the ones with a canonical decomposition) |

### What it does *not* fold — the surprises

| Input | Tokenizer | Why it matters |
|---|---|---|
| `ί` (Greek tonos) | `ί` | the removal covers the Latin range, not the Greek one. A C# implementation that strips every combining mark would *not* agree. |
| `ĳ`, `ǳ`, `ﬀ` | unchanged | the digraphs and ligatures are not decomposed. .NET's `Normalize(FormD)` *does* decompose them — a C# implementation built on NFD would disagree. |
| `İ` | `i` | .NET's `ToLowerInvariant` leaves `İ` alone. |
| `ẞ` | `ß` | and the German transliteration then makes it `ss`. |
| `µ` | `μ` | the Greek mu. .NET keeps the micro sign. |
| `Æ`, `Œ` | `æ`, `œ` | the case is folded, the letter is not decomposed. |

**696 characters of the Basic Multilingual Plane** are folded differently by
the tokenizer and by .NET's `ToLowerInvariant` — and 680 of them differ from
"ToLowerInvariant plus strip the diacritic of the precomposed character",
the rule one would write from the specification. Every one of them is a text
the library would silently fail to find.

⇒ The C# side uses the **measured table**, not a Unicode rule.

### The tokenizer treats the whole text as one token

Spaces and punctuation are *inside* the trigram stream:

```text
"Gedeckter Apfelkuchen" → "gedeckter apfelkuchen"
"E-Mail"                → "e-mail"
"ab cd"                 → "ab cd"
```

That is what makes the trigram tokenizer a substring search — and it is why
the compact column (`s_compact`) exists: a query for `apfelkuchen` does not
match the trigrams of `apfel kuchen`.

### A text shorter than three characters produces nothing

The trigram tokenizer needs three characters. `a`, `ab`, `ß` produce no
tokens at all — which is why the query path has a separate branch for one and
two characters (LUI-Spezifikation §8.1).

---

## 3. The table

`src/Nordmann.LUI/Normalization/tokenizer-fold.txt` holds **1,263 mappings
and 25 removals** — one line per code point of the Basic Multilingual Plane:

```text
# from    to
0041 0061
00C9 0065
0130 0069
0301 -
```

A code point that is not listed is passed through unchanged. A `-` means the
tokenizer drops the character.

The table is an embedded resource; the lookup is a `char[65536]` built once
at startup.

### Regenerate it

```bash
dotnet run --project tools/FoldTableGenerator
```

The generator sweeps every code point of the Basic Multilingual Plane through
the live tokenizer and writes what comes back. It takes about two seconds.

### Verify it

The parity test does the same sweep and compares the result with the table:

```text
The_fold_table_matches_the_tokenizer_for_every_code_point
```

**A table that no longer matches the tokenizer fails the build instead of
lying.** That is the point of the exercise: the C# side and the SQLite side
have to agree, and the only way to know is to ask SQLite.

---

## 4. The parity tests

| Test | What it proves |
|---|---|
| `The_fold_table_matches_the_tokenizer_for_every_code_point` | the table is the tokenizer's behaviour, over the whole BMP |
| `The_normalizer_and_the_tokenizer_agree_on_the_word_corpus` | the C# search form equals the tokenizer's view of the SQL column, for 7,071 German words |
| `The_normalizer_and_the_tokenizer_agree_on_the_full_word_list` | the same for all 685,829 words — the release gate |
| `The_normalizer_and_the_tokenizer_agree_on_the_edge_cases` | the same for the characters a stride cannot reach for |
| `The_edge_cases_hold_the_measured_expectations` | the expectations in the corpus file are the measured ones |
| `The_compact_form_of_the_sql_column_matches_the_compact_form_of_the_normalizer` | the SQL compact column and the C# compact form are the same text |
| `No_folded_character_is_a_separator` | the folding never produces a separator, so stripping before and after the folding agree |

The corpus lives in `tests/Nordmann.LUI.TestData/wordlist/` — a deterministic
stride over the German word list (CC0), plus the edge cases whose behaviour
was measured rather than assumed.

The full list is not checked in. `tools/fetch-wordlist.sh` downloads it; the
test uses it when it is there and skips it when it is not. The pull request
gate runs the sample, the release gate runs the whole list.

---

## 5. What this costs

| | |
|---|---|
| Memory | one `char[65536]` — 128 KB per process, built once |
| Time | one dictionary-free array lookup per character |
| Startup | the table is parsed once, lazily, when the first text is normalised |
| Dependencies | none — the table is measured data, not a Unicode library |

The normaliser is **globalization-independent**: it does not call
`ToLowerInvariant` and does not normalise, so it behaves the same under
`InvariantGlobalization=true` as under ICU.
