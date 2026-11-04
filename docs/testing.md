# The test landscape

> Which test proves what. The specification is the contract
> ([`docs/spec/LUI-Spezifikation.md`](./spec/LUI-Spezifikation.md)); this file
> says which test holds which part of it up — so that a change to a rule can be
> answered in one minute, without reading thirty test files.

**The map is a guard, not a document.** The architecture test
[`Test_Landscape.cs`](../tests/Nordmann.LUI.Architecture.Tests/Test_Landscape.cs)
reads this file and the tree, and fails when they disagree: a test file that
is not listed here, a row that points at a file that does not exist, a layer
that does not match the project the file lives in. A map that rots is worse
than no map — it sends the reader to a test that no longer proves the rule.

---

## The three layers

The standards ask for a test pyramid: unit, architecture, integration. LUI has
three test projects, one per layer, plus the shared infrastructure they all
build on.

| Project | What it holds | How long it takes | Needs a database |
|---|---|---|---|
| [`Nordmann.LUI.Unit.Tests`](../tests/Nordmann.LUI.Unit.Tests) | the rules of the library on their own — the normalisation, the word start, the similarity, the FTS5 expression | **~1 s** | no |
| [`Nordmann.LUI.Integration.Tests`](../tests/Nordmann.LUI.Integration.Tests) | the library against a real SQLite file — the schema, the writes, the query, the sync | **~32 s** | yes, one file per test |
| [`Nordmann.LUI.Architecture.Tests`](../tests/Nordmann.LUI.Architecture.Tests) | the shape of the repository and of the public API | **~0.5 s** | no |
| [`Nordmann.LUI.Testing`](../tests/Nordmann.LUI.Testing) | the shared infrastructure — the database, the corpora, the fixtures. No tests of its own. | — | — |

**The unit layer is the fast one.** It is where a change to a rule is proved:
no file, no connection, no schema — a rule and its table. Everything that needs
SQLite is in the integration layer, because SQLite *is* the behaviour under
test there.

## The gates

Some tests do not run in an ordinary test run. They are not skipped silently —
the reason is in the report.

| Gate | What it switches | Who sets it |
|---|---|---|
| `LUI_PERF=1` | the measurements and the layout experiments | a person, on purpose: [`docs/benchmarks.md`](./benchmarks.md) |
| `LUI_PERF_ENTRIES` | how many entries a measurement builds (default 200,000) | the same |
| `LUI_PERF_KIND=multi` | whether a measurement runs on the multi-word index | the same |
| `LUI_REQUIRE_FULL_WORDLIST=1` | the full 685,829-word parity **must** run, or the test fails | the release gate ([`publish.yml`](../.github/workflows/publish.yml)) |
| `LUI_WORDLIST` | where the full word list is | a person, on purpose |

**A skipped test that reports itself as passed proves nothing.** The full-word-list
parity is skipped — and says so — when the list is missing, rather than
returning green. The release gate sets `LUI_REQUIRE_FULL_WORDLIST=1`, and then
the test fails with the reason instead of skipping.

---

## Unit — the rules on their own

| Test file | Layer | Proves | The rule |
|---|---|---|
| [`NormalizerTests`](../tests/Nordmann.LUI.Unit.Tests/NormalizerTests.cs) | Unit | the search form and the compact form of a text; the German transliteration, on and off; the separators | spec §6.1, ADR-0005 |
| [`SimilarityTests`](../tests/Nordmann.LUI.Unit.Tests/SimilarityTests.cs) | Unit | the four measures: identical texts, unrelated texts, the range, the thread safety | spec §11 |
| [`WordStartTests`](../tests/Nordmann.LUI.Unit.Tests/WordStartTests.cs) | Unit | the word-start table — every row of it, and the hashtag and the hyphen | spec §9.1 |
| [`MultiWordExpressionTests`](../tests/Nordmann.LUI.Unit.Tests/MultiWordExpressionTests.cs) | Unit | the FTS5 expression: the words ANDed, the quoting, the input never copied into the expression raw | spec §8.2 |
| [`NgramDiceEquivalenceTests`](../tests/Nordmann.LUI.Unit.Tests/NgramDiceEquivalenceTests.cs) | Unit | LUI's own N-gram Dice gives the same number as the library it replaces, without allocating | ADR-0011 |
| [`PackageIdentityTests`](../tests/Nordmann.LUI.Unit.Tests/PackageIdentityTests.cs) | Unit | the package identity and the dependency set of the compiled assembly | ADR-0004 |
| [`LuiJsonPayloadSerializerTests`](../tests/Nordmann.LUI.Unit.Tests/LuiJsonPayloadSerializerTests.cs) | Unit | the JSON payload serialiser: the round trip, the UTF-8, the refused null | ADR-0013 |
| [`TypoDictionaryStateTests`](../tests/Nordmann.LUI.Unit.Tests/TypoDictionaryStateTests.cs) | Unit | the states of the typo dictionary — not built, being built, current, stale — and that a query's build does not wait behind another one | ADR-0021 |
| [`RankingTests`](../tests/Nordmann.LUI.Unit.Tests/RankingTests.cs) | Unit | the ranking in groups, and the lazy similarity — the promise that a group that cannot reach the hits costs nothing | ADR-0008 |

## Integration — the library against SQLite

| Test file | Layer | Proves | The rule |
|---|---|---|
| [`DependencyInjectionTests`](../tests/Nordmann.LUI.Integration.Tests/DependencyInjectionTests.cs) | Integration | LUI in a host: the catalog and the indexes as keyed services, ensured on start, the outbox applied on a timer, a stale typo dictionary rebuilt in the background | spec §12.7, ADR-0017, ADR-0018, ADR-0021 |
| [`CatalogTests`](../tests/Nordmann.LUI.Integration.Tests/CatalogTests.cs) | Integration | opening a catalog, the meta table, the lifetime, the clock, the disposal while a query runs | spec §12.1, §12.8 |
| [`EmbeddedIndexTests`](../tests/Nordmann.LUI.Integration.Tests/EmbeddedIndexTests.cs) | Integration | the schema and the write API of an embedded index: `Ensure`, the writes, the payload, the transaction of the application; a database of a pre-release build is refused with a way out | spec §7.2, §12.4 |
| [`LinkedIndexTests`](../tests/Nordmann.LUI.Integration.Tests/LinkedIndexTests.cs) | Integration | a linked index over the table of the application: the table is not touched, the sync, the refresh, the triggers, the outbox | spec §5, §7.3, ADR-0015 |
| [`SyncCheckRebuildTests`](../tests/Nordmann.LUI.Integration.Tests/SyncCheckRebuildTests.cs) | Integration | sync, check, rebuild and optimize — against a source and against itself | spec §12.4, §12.6, §14, ADR-0016 |
| [`SingleWordQueryTests`](../tests/Nordmann.LUI.Integration.Tests/SingleWordQueryTests.cs) | Integration | the query of a single-word index: the prefix path, the trigram candidates, the ranking | spec §8, §9, §12.5 |
| [`MultiWordIndexTests`](../tests/Nordmann.LUI.Integration.Tests/MultiWordIndexTests.cs) | Integration | the multi-word index: the schema, the candidates, the ranking of names and titles — and Anhang B | spec §8.2, §9, ADR-0011 |
| [`GoldenQueryTests`](../tests/Nordmann.LUI.Integration.Tests/GoldenQueryTests.cs) | Integration | the golden queries as fixed expectations — the system tag first among 30,000 hits | spec §19, Anhang B |
| [`NormalizationParity`](../tests/Nordmann.LUI.Integration.Tests/NormalizationParity.cs) | Integration | the rule exists twice — as SQL and as C# — and both give the same answer, for every code point and for every word of the list | spec §6.2, §19, ADR-0005 |
| [`FoldProbeTests`](../tests/Nordmann.LUI.Integration.Tests/FoldProbeTests.cs) | Integration | the probe that asks the loaded SQLite how it folds — and that it finds a table which folds differently | ADR-0007 |
| [`QueryPlanTests`](../tests/Nordmann.LUI.Integration.Tests/QueryPlanTests.cs) | Integration | the plan of the candidate query: the search index in the rowid order, no sorter | spec §8.2, §19 |
| [`SchemaGoldenTests`](../tests/Nordmann.LUI.Integration.Tests/SchemaGoldenTests.cs) | Integration | the schema of an embedded index is the golden one — byte for byte | ADR-0014, ADR-0015 |
| [`TypoToleranceTests`](../tests/Nordmann.LUI.Integration.Tests/TypoToleranceTests.cs) | Integration | a typo does not lead nowhere: the four kinds of edit, the correction, the limits | spec §10, ADR-0012 |
| [`TypoDictionaryLifecycleTests`](../tests/Nordmann.LUI.Integration.Tests/TypoDictionaryLifecycleTests.cs) | Integration | a query does not wait for a build that runs and says so; a sync marks the dictionary stale, a rebuild makes it current | spec §10.1, §10.3a, ADR-0021 |
| [`TypoQualityTests`](../tests/Nordmann.LUI.Integration.Tests/TypoQualityTests.cs) | Integration | the quality of the correction against a measured floor, by the length of the beginning | spec §10.4, §19, Anhang A4 |
| [`ConcurrencyTests`](../tests/Nordmann.LUI.Integration.Tests/ConcurrencyTests.cs) | Integration | reads in parallel, writes serial — neither disturbs the other | spec §15 |
| [`ApiShapeTests`](../tests/Nordmann.LUI.Integration.Tests/ApiShapeTests.cs) | Integration | the shape of the API before 0.1.0: the entry, the batch, the options that check themselves, the read and the write interface | ADR-0013 |
| [`RowIdTests`](../tests/Nordmann.LUI.Integration.Tests/RowIdTests.cs) | Integration | the rowid carries the priority and the weight — and the SQL and the C# agree on it | spec §7.2 |
| [`ReviewFollowUpTests`](../tests/Nordmann.LUI.Integration.Tests/ReviewFollowUpTests.cs) | Integration | the findings of the review of pull request 6, each reproduced red before it was fixed | review of PR #6 |
| [`TypoReviewTests`](../tests/Nordmann.LUI.Integration.Tests/TypoReviewTests.cs) | Integration | the findings of the review of pull request 10, each reproduced red before it was fixed | review of PR #10 |

### The measurements — they run on purpose

They are not tests of behaviour; they are the numbers in
[`docs/benchmarks.md`](./benchmarks.md). They are skipped unless `LUI_PERF=1`. The corpus they measure is
generated by `Corpus` in `Nordmann.LUI.Testing` — the same one the benchmark project
(`benchmarks/Nordmann.LUI.Benchmarks`) measures (ADR-0020).

| Test file | Layer | Measures | The number |
|---|---|---|
| [`PerformanceBudgets`](../tests/Nordmann.LUI.Integration.Tests/PerformanceBudgets.cs) | Integration | **the seven targets of §16 in their scenario** — 3 M entries, the whole word list, 1 M titles; each one met and asserted, or waived with a reason | spec §16, ADR-0020 |
| [`QueryPerformance`](../tests/Nordmann.LUI.Integration.Tests/QueryPerformance.cs) | Integration | the latency and the allocation of a query, the cost of a write, the build | spec §16 |
| [`SyncPerformance`](../tests/Nordmann.LUI.Integration.Tests/SyncPerformance.cs) | Integration | what a sync costs, a cancelled one, the check, the rebuild — and a delta sync beside a built typo dictionary | spec §20 step 7, ADR-0016, ADR-0021 |
| [`TypoPerformance`](../tests/Nordmann.LUI.Integration.Tests/TypoPerformance.cs) | Integration | what the typo dictionary costs, and how good the correction is on the whole list | spec §16, ADR-0012 |
| [`LinkedPerformance`](../tests/Nordmann.LUI.Integration.Tests/LinkedPerformance.cs) | Integration | what a linked index costs against the table of the application | ADR-0015 |
| [`BatchPerformance`](../tests/Nordmann.LUI.Integration.Tests/BatchPerformance.cs) | Integration | what a batch saves over a loop of single upserts | ADR-0013 |
| [`FtsLayoutExperiment`](../tests/Nordmann.LUI.Integration.Tests/FtsLayoutExperiment.cs) | Integration | the FTS5 layout, one setting varied at a time | ADR-0010 |
| [`MultiWordLayoutExperiment`](../tests/Nordmann.LUI.Integration.Tests/MultiWordLayoutExperiment.cs) | Integration | one indexed column or two | ADR-0011 |
| [`LinkedLayoutExperiment`](../tests/Nordmann.LUI.Integration.Tests/LinkedLayoutExperiment.cs) | Integration | the spike of the linked layout | issue #15 |
| [`EmbeddedBulkExperiment`](../tests/Nordmann.LUI.Integration.Tests/EmbeddedBulkExperiment.cs) | Integration | the bulk path, measured before it was built | ADR-0010, PR-007 |

## Architecture — the shape of the repository

| Test file | Layer | Proves | The rule |
|---|---|---|
| [`Internal_Layering`](../tests/Nordmann.LUI.Architecture.Tests/Internal_Layering.cs) | Architecture | the inside of the library has a direction: a namespace knows only the ones below it, and no layer reaches up through the facade | ADR-0014 |
| [`Public_API_Shape`](../tests/Nordmann.LUI.Architecture.Tests/Public_API_Shape.cs) | Architecture | the public API is a promise: no web stack, no `Task`, one namespace, one prefix, no positional constructors, and the content sources are called `Embedded` and `Linked` — the retired word `Owned` is gone | spec §13, ADR-0013, ADR-0023 |
| [`Repository_Layout`](../tests/Nordmann.LUI.Architecture.Tests/Repository_Layout.cs) | Architecture | the build wiring is part of the contract: one place for the versions, exactly one packable project, the documented dependency set | ADR-0001, ADR-0002, ADR-0004 |
| [`Namespace_Convention`](../tests/Nordmann.LUI.Architecture.Tests/Namespace_Convention.cs) | Architecture | folder = namespace, every namespace below the root | spec §17.2 |
| [`Readme_Benchmarks`](../tests/Nordmann.LUI.Architecture.Tests/Readme_Benchmarks.cs) | Architecture | the BenchmarkDotNet tables of the README are rendered from the committed reports of one run; every benchmark class has a report and a section | ADR-0022 |
| [`Test_Landscape`](../tests/Nordmann.LUI.Architecture.Tests/Test_Landscape.cs) | Architecture | this map is the truth: every test file is listed here, every row points at a file that exists; the unit layer opens no database | this file, ADR-0019 |

---

## The shared infrastructure

[`Nordmann.LUI.Testing`](../tests/Nordmann.LUI.Testing) holds what the tests
share. It has no tests of its own — it is the setup, in one place.

| Type | What it is for |
|---|---|
| [`TestDatabase`](../tests/Nordmann.LUI.Testing/TestDatabase.cs) | a database file of its own for one test, deleted afterwards. A file and not `:memory:` — every connection of the pool must see the same database. |
| [`Corpus`](../tests/Nordmann.LUI.Testing/Corpus.cs) | the generated corpus of the measurements: single words (the word list, then compounds) or titles, weights with a long tail or sparse, the inputs users type and the typos — the same for a seed, for the budget tests and the benchmarks alike (ADR-0020) |
| [`TestCorpus`](../tests/Nordmann.LUI.Testing/TestCorpus.cs) | where the corpora are: the sample word list, the edge cases, the full word list, the golden schema. One place, because eight tests each built their own path out of the repository root — and a path that is spelled eight times is wrong once. |
| [`ApplicationTable`](../tests/Nordmann.LUI.Testing/ApplicationTable.cs) | the table of the application, as a linked index sees it |
| [`RecipeStock`](../tests/Nordmann.LUI.Testing/RecipeStock.cs) | the stock of recipe titles the multi-word tests ask for |
| [`Chip`](../tests/Nordmann.LUI.Testing/Chip.cs) | the payload the integration tests attach to an entry. The unit layer has one of its own, so that it does not depend on the infrastructure that opens databases |
| [`TypoMaker`](../tests/Nordmann.LUI.Testing/TypoMaker.cs) | makes the typos the tests type |
| [`Repository`](../tests/Nordmann.LUI.Testing/Repository.cs) | finds the repository on disk — the architecture rules read the *source*, not a hand-written list |

## The test data

[`Nordmann.LUI.TestData`](../tests/Nordmann.LUI.TestData) holds the corpora —
no project, no code, just the files the tests read.

| File | What it is |
|---|---|
| [`wordlist/german-words.sample.txt`](../tests/Nordmann.LUI.TestData/wordlist/german-words.sample.txt) | every 97th word of the German word list — 7,071 words, deterministic, checked in |
| [`wordlist/edge-cases.txt`](../tests/Nordmann.LUI.TestData/wordlist/edge-cases.txt) | the characters a stride cannot reach for: umlauts, ß, accents, ligatures, decomposed forms |
| [`golden/embedded-schema.txt`](../tests/Nordmann.LUI.TestData/golden/embedded-schema.txt) | the `sqlite_master` of an embedded index, as it was before the second content source |

The full list (685,829 words) is **not** checked in — 5.5 MB that nobody
reviews. [`tools/fetch-wordlist.sh`](../tools/fetch-wordlist.sh) puts it in
`artifacts/wordlist/`, and the release gate fetches it and demands it.

## How to run what

```bash
# the fast layer — the rules, without a database
dotnet test tests/Nordmann.LUI.Unit.Tests

# the layer against SQLite
dotnet test tests/Nordmann.LUI.Integration.Tests

# the shape of the repository
dotnet test tests/Nordmann.LUI.Architecture.Tests

# everything, as CI runs it
dotnet test Lui.sln

# a measurement, on purpose
LUI_PERF=1 LUI_PERF_ENTRIES=3000000 dotnet test tests/Nordmann.LUI.Integration.Tests --filter QueryPerformance
```

## The rules the tests follow

| Rule | Where it comes from |
|---|---|
| The name of a test is a sentence that names the rule, not the method | the standards |
| A test that proves nothing is not green — it is skipped with a reason | ADR-0010, the full-word-list gate |
| A measurement is never a test of behaviour | ADR-0010 |
| The specification is the contract; a test names the paragraph it holds | `AGENTS.md` |
| A change to a rule comes with a change to the test that holds it | `AGENTS.md` |
