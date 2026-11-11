# Plan — The public surface of LUI and the measurement of its performance

- **Date:** 2026-11-11
- **Base branch:** `dev` (the pull requests branch from it; `dev` → `main` is a release pull request)
- **Decided by:** the owner, in six rounds on 2026-11-11
- **Issues:** #117 (the epic of this plan), #118–#125 (the eight tasks, one per pull request)
- **Status:** agreed — not started. The first pull request is half-written in the working tree.
- **Amended:** 2026-11-11, after the sixth round: the restructuring of the test projects was missing from the first
  draft and is now D17–D19 and PR 5. The plan is still written before the work; the amendment is recorded here rather
  than silently applied.
- **Why now:** two reasons that share one deadline. The public API may still break **until the tag `v0.1.0` exists**
  (ADR-0026) — after that a break costs a minor version. And the performance numbers that are published today were
  measured on a corpus that was not the corpus they claim to be.

---

## 1. The goal

Three goals, one plan:

1. **The public surface.** The API that a reader of the documentation meets must be the API that exists: the typo
   distance is an enumeration, one option that promised more than it delivered is gone, and the repository no longer
   depends on a document that is about to be deleted.
2. **The measurement.** The budgets live in a file, the corpora are fixed files, every measurement names the corpus it
   ran on, and the performance lane runs weekly instead of nightly.
3. **The repository as a public thing.** Everything is English, nothing refers to a private contract that a reader
   cannot know, and the specification — a German document that is the contract in `AGENTS.md` — is unlinked first and
   deleted afterwards.

---

## 2. What the audit found (the reason for the plan)

The audit of 2026-11-11 (report outside the repository) walked the nine budgets of §16 against the artefacts in
`benchmarks/results/`. The budgets were met — and the measurement behind them does not hold:

| Finding | Evidence |
|---|---|
| **The corpus was not the corpus it claimed to be.** `Corpus.Generate` takes the *first* 200 000 words of the word list when the size is 200 000, and 2.3 M compounds when it is 3 000 000. At 200 000 entries the corpus held no compound at all and only the letters a–f: the input `ka` found **0** hits, `b` found 45 926, `haus` 424, `apfelkuc` 1. | `tests/Nordmann.LUI.Testing/Corpus.cs`, the committed BDN reports |
| **The multi-word measurement measured nothing.** The same input found 0 hits in the multi-word case; the error of the line was 20.3 % with a standard deviation of 58.9 %. | `benchmarks/results/…QueryBenchmarks-report-github.md` |
| **The provenance of the reports contradicts the documentation.** The reports name a Xeon 6982P-C at 3.80 GHz with 2 cores on Debian 12; `docs/benchmarks.md:426` names a Xeon at 2.10 GHz with 4 cores on Ubuntu. | the reports vs. `docs/benchmarks.md` |
| **A committed report without a date, an iteration count and a `journal_mode` is not a proof.** The three write lines are 5–7× slower than the CPU lines while the code did not change — the cost per commit of an fsync, and no write-ahead log in the measured file. | the write report vs. the query report |
| **A committed report says 457 MB ≤ 500 MB "met" and 457 MB ≤ 300 MB "missed" in the same repository.** | `docs/benchmarks.md:403` against the report |
| **`DistinctText` costs more than it earns.** It keeps the folded text of every candidate: **+28 KB** per query
  (single word), **+84 KB** (multi word). A duplicate also **uses up the candidate budget**, so the option makes the
  search shallower, not only the answer shorter. | ADR-0032, `src/Nordmann.LUI/Querying/CandidateReader.cs` |
| **The typo distance is a free number today.** `TypoDictionary` hardcodes `maxDictionaryEditDistance: 1` and looks up
  with distance 1 — the dictionary cannot be built for two errors at all. | `src/Nordmann.LUI/Typos/TypoDictionary.cs` |

The lesson that runs through all of it: **a number without its corpus is not a result.** Every decision below follows
from that.

---

## 3. The decisions

### 3.1 The public surface

| # | Decision | Why |
|---|---|---|
| D1 | **The typo distance is an enumeration** `LuiTypoDistance` (`None`, `OneError`, `TwoErrors`) on `ILuiIndex`, in `IndexOptions` and in `IndexDefinition`. The builder takes `TypoTolerance(LuiTypoDistance distance = LuiTypoDistance.OneError)`; the **query** keeps `TypoTolerance(bool)` — a query switches the correction on or off, the distance belongs to the index. | A distance is a number a user cannot choose freely: the dictionary grows with every error. A number is also not extensible — an enumeration is. |
| D2 | **The distance belongs to the stored definition.** An index knows how its dictionary was built, also after a restart. | The old state was an artefact of the builder-only setting: an opened index never corrected, unless the query asked. |
| D3 | **An opened index corrects when its definition says so.** | An index that forgives typos should do so after a restart too. A behaviour change ⇒ an ADR and a note in the CHANGELOG. |
| D4 | **`TypoDistance.None` builds no dictionary at all.** | 140 MB for nothing is not a setting. |
| D5 | **`DistinctText` is removed before the freeze.** The API (`LuiQueryOptions.DistinctText`, `LuiQuery.DistinctText`), the sample checkbox, the tutorial, the README list, `docs/api.md` and the five tests go. The uniqueness of a text belongs to the data — a unique constraint on the normalised text, the application's own constraint in the `Linked` case. | It is a data problem with a query-time patch; the patch is the most expensive option in the library (ADR-0032) and it makes the search shallower. |

### 3.2 The measurement

| # | Decision | Why |
|---|---|---|
| D6 | **The budgets live in one file**: `tests/Nordmann.LUI.Testing/Budgets/budgets.json` as an embedded resource, read by a `LuiBudgets` API. `PerformanceBudget` and `ReadmeBudgetsTests` read it instead of hardcoded numbers. **JSON, not YAML** — .NET has no YAML reader in the box, and a new dependency for a file format is an ADR and a package. | One source, no path logic, the file travels with the assembly. |
| D7 | **The corpora are fixed files, measured on 2026-11-11 and checked in.** `tests/TestData/corpora/words.txt` — 1 908 798 German words (source: `kkrypt0nn/wordlists`, `wordlists/languages/german.txt`; **that repository has no licence entry for this list** and its catalogue says the source is *not known*). `tests/TestData/corpora/movies.tsv` — 651 587 unique movie titles with the number of votes (source: `datasets.imdbws.com`, `title.basics.tsv` + `title.ratings.tsv`). `tests/TestData/corpora/books.tsv` — 1 601 unique book titles with the number of reviews (source: `luminati-io/Amazon-popular-books-dataset` — a scrape without a licence, and a sample of a paid product). `tests/TestData/corpora/names.txt` — the owner delivers it. No manifest: the file name is the identity. The word list also **supplies the sample**: `tests/TestData/wordlist/german-words.sample.txt` becomes the stride (97) over `words.txt`, and the download of the list (`tools/fetch-wordlist.sh`) and the path override `LUI_WORDLIST` go. | A corpus that changes with the scaling parameter is a different corpus (see §2). A list that is fetched from the network and a list that is measured are two truths; checked in, it is one. The licences are recorded because a public repository must be able to answer *may this be redistributed?* — where the answer is *no*, the corpus is the owner's own. |
| D8 | **The loader reads `text` or `text<TAB>weight`.** Without a weight column the weight is derived deterministically (the existing long-tail rule, with a seed). | The owner can hand over a plain list today and real weights later without a format break. |
| D9 | **A corpus is a *prefix* of the file — and the order of the corpus is a deterministic permutation of the file** (by the hash of the text), the same rule for every corpus. A prefix is therefore always a representative sample, never *the alphabetically first N* — which is exactly the bug of the old generator. The scenario carries the prefix length and the minimum weight; the corpus file carries everything. | The order of the file decides what a prefix means. The IMDb list is sorted by title, the books by popularity, the words alphabetically — and a prefix of an alphabetical list is the audit's bug all over again (at 200 000 entries: the letters a to f). One rule for every corpus, independent of how the file happens to be sorted. |
| D10 | **BenchmarkDotNet gets a linked fixture, a hit count per row and a settings axis** (candidates, order, similarity, correction on/off). | A line without its hit count can measure nothing and look healthy. |
| D11 | **The performance lane runs weekly** (`17 3 * * 1` UTC), the build lane does not run it. No environment variables configure it — the workflow input replaces them. | A weekly lane is enough for a comparison; a nightly lane burns the runner. |
| D17 | **The measurement becomes its own layer.** Everything that reads the clock moves into `tests/Nordmann.LUI.Performance.Tests`: `BudgetPerformance`, `QueryPerformance`, `TypoPerformance`, `SyncPerformance`, `LinkedPerformance`, `BatchPerformance`. **`BudgetMachineTests` goes to the unit layer** — it tests the logic of the budget (`ScenarioEntries`, `BelongsToTheMachine`) without a database, so it is a rule and not a measurement. The tree has three layers plus the measurement layer; ADR-0019 is restated, with ADR-0041 and ADR-0050. | A measurement in the integration layer is a measurement that runs when somebody remembers the variable. A rule test that reads the clock's rules belongs with the rules. |
| D18 | **The environment variables die:** `LUI_PERF`, `LUI_PERF_ENTRIES`, `LUI_PERF_KIND`, `LUI_PERF_MACHINE` and `LUI_REQUIRE_FULL_WORDLIST`. What replaces them is a **scenario file** that the lane writes (`performance.scenario.json`): the corpus, the prefix length, the settings. The file is checked in with the default scenario, so a run without the lane works too. | A variable in the environment is invisible in the code and in the report. A file is a thing one can read, check in and diff. |
| D19 | **The guard that the full word list is there moves to the fetch.** `tools/fetch-wordlist.sh` already checks the 685 829 lines and fails loudly (`set -euo pipefail`); the CI step that runs the parity over the whole list keeps its explicit filter. | The variable was the second guard for the same fact. Two guards for one fact are one too many — and the fetch is the one that knows the number. |

### 3.3 The repository

| # | Decision | Why |
|---|---|---|
| D12 | **The specification is unlinked first, deleted afterwards.** `AGENTS.md` states the rules itself ("this repository's own rules; the decisions live in the ADRs and the docs"); the budgets move into the file of D6. The deletion is a separate pull request — it touches 175 files. | A big deletion mixed into the work is neither reviewable nor revertible on its own. |
| D13 | **The §-references are rehung, not dropped.** Every `§x` becomes the ADR or the documentation page that carries the same rule; where none exists, the reference goes. The pull request delivers the mapping table. | A cross-reference to a deleted document is a dead end; a cross-reference to the decision that carries the rule is the same pointer, alive. |
| D14 | **Every reference to the Nordmann standards goes** — `AGENTS.md`, `docs/architecture/alignment.md` (the file *is* about them), `docs/testing.md`, the README, `Directory.Build.props`, the CI comment, the ADRs (restated in place with a dated note) and the working papers. | LUI is a public library. Nobody outside knows what the standards are, and nobody has to. |
| D15 | **The repository is English.** The specification is **not** translated — it is deleted (D12). The two German working papers are translated, the five tutorials get English file names. **The German sample data stays German** ("Gedeckter Apfelkuchen"): it shows what the normalisation is for. | Translating 1430 lines of a document that is about to be deleted is work without a purpose. |
| D16 | **The base branch is `dev`.** The pull requests branch from it; `dev` → `main` is a release pull request with its own review. | The work is large and interdependent; the API breaks belong together, before the tag `v0.1.0`. |

---

## 4. The order: eight pull requests

Every pull request: its own branch off `dev`, its own ADR in the same pull request, a note in the `CHANGELOG`, its tests,
and a measurement where a number is claimed. The acceptance bar is written below.

### PR 1 — The typo distance is an enumeration (D1, D2, D3, D4)

- The change that is half-written in the working tree: `LuiTypoDistance`, the builder, the interfaces, the definition,
  the dictionary, the catalog.
- ADR 0054 (topic: *Typo tolerance*), CHANGELOG (breaking, before the freeze), `docs/api.md`.
- **`docs/typo-dictionary.md`** — how the memory is built (word beginnings of 3–10 characters, the delete variants of
  SymSpell) **with the measured numbers per distance.** The numbers for two errors do not exist yet: they are measured
  before the document is written.
- Tests: the distance reaches the definition; an opened index keeps its distance (D2/D3); `None` builds no dictionary
  (D4); a correction with two errors works on a small corpus.
- **Acceptance bar:** the build is green, the four test projects are green, `docs/testing.md` has a line for the new
  test file, `tools/mutation-check.sh` has an entry for the changed rule.

### PR 2 — `DistinctText` is removed (D5)

- The API, the sample, the tutorial, the README, `docs/api.md`, the five tests (restated, not deleted: the *case* —
  one text in two spellings — becomes a statement about the data, in the documentation).
- ADR 0055 (topic: *Query path*), CHANGELOG with the migration note.
- **Acceptance bar:** no `DistinctText` remains in the public API, the sample still runs, the tutorial still compiles
  (its code blocks are run as one program).

### PR 3 — The budgets in a file, the specification unlinked (D6, D12, D14)

- `budgets.json` + the `LuiBudgets` API; `PerformanceBudget` and `ReadmeBudgetsTests` read it; the nine budgets of §16
  (the seven rows of its table, as the README lists them) are the first content.
- `AGENTS.md` in its new form: the rules of this repository, no "the specification is the contract", no standards.
- The standards references out of `AGENTS.md`, `docs/architecture/alignment.md` (restated as "what applies to a
  library"), `docs/testing.md`, the README, `docs/README.md`, `Directory.Build.props`, the CI comment.
- ADR 0056 (topic: *Measurement*).
- **The two budget mutations change with it:** `a-budget-limit-in-the-readme-edited-by-hand` and
  `a-budget-limit-in-the-record-edited-by-hand` edit the **README** today. After this pull request the limit lives in the
  JSON file, so both mutations edit **the file** — the guard stays the same, the target moves.
- **Acceptance bar:** the budget test fails when a number in the file is wrong (a mutation), and the README renderer
  reads the same file — one source, two readers.

### PR 4 — The documentation is English and free of the standards (D14, D15)

- The two German working papers translated (about 520 lines), the five tutorials renamed
  (`01-first-index.md` …), the ADR texts restated without the standards (with a dated note), the ADR index updated.
- **Acceptance bar:** a search for the German function words over the repository finds nothing outside the sample data;
  a search for the standards finds nothing.

### PR 5 — The measurement becomes its own layer (D17, D18, D19)

- The project `tests/Nordmann.LUI.Performance.Tests` (xUnit) and the six files that read the clock move into it
  (D17). `BudgetMachineTests` goes to the unit layer — it is a rule test, not a measurement.
- The guards that watch the tree change with it, or they fail the build:
  - `Architecture.Tests/TestLandscapeTests.cs` — `Every_test_project_of_the_tree_has_a_layer_of_the_map`; the new
    project needs a row in `Layers` (the failure message says exactly that: *"a new one is a new row of the list of
    the layers"*).
  - `Architecture.Tests/TimingTraitTests.cs` — `Every_test_that_reads_the_clock_carries_the_timing_trait` scans every
    test project; the new one is included.
  - `docs/testing.md` — the map is a guard: every file needs a row, and the layer table of the "three layers" grows a
    fourth.
- `Support/FullWordListFactAttribute.cs` moves into the shared infrastructure (`Nordmann.LUI.Testing`), because
  `NormalizationParityTests` uses it and stays in the integration layer. `Support/PerformanceFactAttribute.cs` and
  `Support/TestGates.cs` go: with the measurement in its own layer, the gate is the layer (D18).
- The environment variables go with them (D18): the **scenario file** (`performance.scenario.json`) takes their place,
  checked in with the default scenario. The guard for the full word list moves to the fetch (D19).
- ADR 0057 (topic: *Measurement*), the restatement of ADR-0019 ("three layers" → "three layers and the measurement
  layer"), ADR-0041 and ADR-0050.
- **Acceptance bar:** the four test projects and the new one are green; the landscape test, the timing trait test and
  the map agree with the tree; `grep LUI_PERF` over the repository finds nothing.

### PR 6 — The measurement infrastructure (D7, D8, D9)

- The three corpora and their loader (D7, D8), the measurement store (`benchmarks/measurements/<project>.json`),
  `docs/benchmarks.md` with the measured numbers.
- The old generator (`Corpus.Generate`) and the fallback to the 7 071-word sample with `LUI_WORDLIST` go.
- **Acceptance bar:** the build matrix runs over every setting combination on a prefix of the corpora; the index
  build is measured once (not fifteen times by the benchmark); every result names its corpus.

### PR 7 — The benchmarks grow up, the lane runs weekly (D10, D11)

- The linked fixture, the hit count per row, the settings axis, the compact renderer in the README with the link to
  `benchmarks/results/`.
- `ci.yml`: the trigger becomes `[main, dev]` — **without this the whole series runs without a CI**.
- `performance.yml`: the cron becomes `17 3 * * 1`; the lane measures `main` (the stable line), `dev` is measured by
  hand.
- **Acceptance bar:** a report line with zero hits is visible as zero hits.

### PR 8 — The specification is deleted, the §-references are rehung (D12, D13, D16)

- The deletion of `.agents/spec/LUI-Spezifikation.md` and the mapping table: `§x` → the ADR or the page that carries
  the rule; where none exists, the reference goes.
- **Acceptance bar:** no `§` reference points to a document that does not exist; the build and the five test projects
  are green.

---

## 5. The branch model and what it changes

`dev` is the base. The pull requests target `dev`; `dev` → `main` is a release pull request with its own review. The
rules that follow from it:

| Where | What changes |
|---|---|
| `.github/workflows/ci.yml` | The trigger is `push: [main]` and `pull_request: [main]`. **It must become `[main, dev]`** — otherwise no pull request of this series is checked at all. |
| `.github/workflows/performance.yml` | The schedule always runs the **default branch**. `main` stays the default branch ⇒ the weekly number is the stable line; `dev` is measured by hand (`workflow_dispatch`). |
| `.github/workflows/publish.yml` | Unchanged (tag `v*`). The tag must point at a commit of `main` ⇒ the release comes out of the `dev` → `main` pull request. |
| `AGENTS.md` | "Never straight to `main`" becomes "the pull requests target `dev`; `dev` → `main` is a release pull request". |
| ADR-0026 | Unchanged, and it is what makes the order right: the breaks are free **before** the tag `v0.1.0` and cost a minor version after it. The series therefore lands on `main` before the tag. |

---

## 6. The traps that are known

| Trap | What it costs |
|---|---|
| The `dev` branch without the CI trigger | A whole series lands unchecked. This is the one that fails silently. |
| A measurement without its corpus | Two numbers that look comparable and are not. Every result names file and prefix length (D9). |
| A report without a date, an iteration count and the `journal_mode` | The write lines of the current reports are 5–7× slower than the CPU lines **without a change in the code** — a constant cost per commit (fsync), and no write-ahead log in the measured file. |
| A budget that is quietly lowered | The target is a promise (ADR-0040). A budget that is missed is re-measured and re-decided, never lowered in passing. |
| A typo distance as a free number | The dictionary grows with every error; the numbers of D1 exist to be measured (PR 1), not guessed. |
| An English sweep that eats the English sample data | The umlauts and the ß are the point of the sample. They stay. |
| A deletion that is mixed into the work | The deletion touches 175 files. It is its own pull request (D12). |
| A new test project without the map and the guards | `TestLandscapeTests` and `TimingTraitTests` watch the tree — a project that is not in the map fails the build, and the failure says in one sentence what to do. Read it before guessing (D17, PR 5). |
| A rule test that travels with the measurements | `BudgetMachineTests` checks the logic of the budget without a database; if it travels into the weekly lane, the build lane loses that guard (D17). |

---

## 7. What is not decided here

- **The names corpus and the size of the titles corpus.** The words, the movies and the books are measured (D7); the
  names file comes from the owner. The budgets of D6 name the corpora by file, not by a hardcoded entry count; the
  numbers of §16 that name a count (3 000 000 entries, 1 000 000 titles, 700 000 words) are re-decided against the
  measured corpora — and the title length changes with them (the IMDb titles average 19 characters, the budget
  assumes 36).
- **The measured numbers of PR 1** (the memory of the dictionary per distance). They are measured before
  `docs/typo-dictionary.md` is written.
- **The mapping table of PR 8.** It is written with the deletion, section by section.
- **The optimisations of the audit** (the prefix path reads 501 rows and uses 20 of them; the statements are prepared
  per query; the correction is eager over three variants; the build writes twice). They are not part of this plan —
  they are a separate line of work with their own decisions.

---

## 8. The record of the decisions

The decisions of this plan become ADRs in the same pull request as the change they describe — **one per pull request**:

| Pull request | ADR | Topic |
|---|---|---|
| 1 — the typo distance | 0054 | Typo tolerance |
| 2 — `DistinctText` goes | 0055 | Query path |
| 3 — the budgets in a file | 0056 | Measurement |
| 4 — English and free of the standards | 0057 | Documentation |
| 5 — the measurement layer | 0058 | Measurement |
| 6 — the corpora | 0059 | Measurement |
| 7 — the benchmarks and the lane | 0060 | Measurement |
| 8 — the specification goes | 0061 | Documentation |

The number of an ADR is the next free one at the time the pull request opens; the table says what the plan expects.

The issues exist: **#117** is the epic of this plan, **#118–#125** are the eight tasks, one per pull request.

The plan is written before the work and is not rewritten afterwards — the amendments of 2026-11-11 are recorded in the
header.
