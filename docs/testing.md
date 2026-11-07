# The test landscape

> Which test proves what. The specification is the contract of the library; this
> file says which test holds which part of it up — so that a change to a rule can
> be answered in one minute, without reading thirty test files.

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
| `LUI_PERF_MACHINE=other` | the machine of the run: `measurement` asserts every target, `other` reports the times and asserts the sizes | the lane (`.github/workflows/performance.yml`) |

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
| [`JaroWinklerEquivalenceTests`](../tests/Nordmann.LUI.Unit.Tests/JaroWinklerEquivalenceTests.cs) | Unit | LUI's own Jaro-Winkler against the library it replaces: the same number for known pairs, 10,000 random pairs, the lengths 0 … 12 — without a tolerance | issue #30, ADR-0032 |
| [`DamerauEquivalenceTests`](../tests/Nordmann.LUI.Unit.Tests/DamerauEquivalenceTests.cs) | Unit | LUI's own Damerau-Levenshtein against the library it replaces: the same number, without a tolerance, and no allocation | issue #30, ADR-0030 |
| [`LevenshteinEquivalenceTests`](../tests/Nordmann.LUI.Unit.Tests/LevenshteinEquivalenceTests.cs) | Unit | LUI's own Levenshtein against the library it replaces: the same number, without a tolerance, and no allocation | issue #30, ADR-0030 |
| [`SearchFormParityTests`](../tests/Nordmann.LUI.Unit.Tests/SearchFormParityTests.cs) | Unit | the search form in the buffer and as a string: the same form — the ranking measures in the buffer | issue #30, ADR-0032 |
| [`WordStartTests`](../tests/Nordmann.LUI.Unit.Tests/WordStartTests.cs) | Unit | the word-start table — every row of it, and the hashtag and the hyphen | spec §9.1 |
| [`MultiWordExpressionTests`](../tests/Nordmann.LUI.Unit.Tests/MultiWordExpressionTests.cs) | Unit | the FTS5 expression: the words ANDed, the quoting, the input never copied into the expression raw | spec §8.2 |
| [`NgramDiceEquivalenceTests`](../tests/Nordmann.LUI.Unit.Tests/NgramDiceEquivalenceTests.cs) | Unit | LUI's own N-gram Dice gives the same number as the library it replaces, without allocating | ADR-0011 |
| [`PackageIdentityTests`](../tests/Nordmann.LUI.Unit.Tests/PackageIdentityTests.cs) | Unit | the package identity and the dependency set of the compiled assembly | ADR-0004 |
| [`KeyCodecTests`](../tests/Nordmann.LUI.Unit.Tests/KeyCodecTests.cs) | Unit | the key between the application and the file: a key of the other kind refused with the index and both kinds, the stored text, the names of the kinds the files already carry, a name of a kind this version does not know | ADR-0035 |
| [`LuiKeyTests`](../tests/Nordmann.LUI.Unit.Tests/LuiKeyTests.cs) | Unit | the key: a number or a text without a cast, back with one; `1` is not `"1"`; the conversions out of it throw for the other kind | ADR-0035 |
| [`LuiPayloadSerializerTests`](../tests/Nordmann.LUI.Unit.Tests/LuiPayloadSerializerTests.cs) | Unit | the JSON serialiser of the catalog: the round trip, the UTF-8, the type of the instance, a type missing from the context named on the write and the read, the strict reading (a type of another shape refused, a missing constructor parameter refused, a type with every member and optional ones besides read), the refused null | ADR-0013, ADR-0035 |
| [`TypoDictionaryStateTests`](../tests/Nordmann.LUI.Unit.Tests/TypoDictionaryStateTests.cs) | Unit | the states of the typo dictionary — not built, being built, current —, that it only grows, that every index has its own, and that a query's build does not wait behind another one | ADR-0021, ADR-0025 |
| [`RankingTests`](../tests/Nordmann.LUI.Unit.Tests/RankingTests.cs) | Unit | the ranking in groups, and the lazy similarity — the promise that a group that cannot reach the hits costs nothing | ADR-0008 |

## Integration — the library against SQLite

| Test file | Layer | Proves | The rule |
|---|---|---|
| [`DependencyInjectionTests`](../tests/Nordmann.LUI.Integration.Tests/DependencyInjectionTests.cs) | Integration | LUI in a host: the catalog is the one service of the container, no index is registered, the settings of the container are its own, the catalog goes with the host | spec §12.7, ADR-0017, ADR-0018, ADR-0033 |
| [`CatalogTests`](../tests/Nordmann.LUI.Integration.Tests/CatalogTests.cs) | Integration | opening a catalog, the meta table, the lifetime, the clock, the disposal while a query runs | spec §12.1, §12.8 |
| [`ReadPathTests`](../tests/Nordmann.LUI.Integration.Tests/ReadPathTests.cs) | Integration | the read path: an index that is opened and not created — the index that is not there, a foreign schema version, a foreign folding, nothing written, no write lock, a read-only file, the kind of the key from the file and a key of the other kind, the payload read by the serialiser of the catalog that opens the file, a payload that cannot be read as the type asked for, a payload read as a type of another shape, whatever a serialiser of the application throws, the definition of an older version and of an unknown key type, an index opened as what it is, a linked index with a payload | ADR-0033, ADR-0035, issues #56, #58 |
| [`ReadOnlyCatalogTests`](../tests/Nordmann.LUI.Integration.Tests/ReadOnlyCatalogTests.cs) | Integration | the catalog on a file that may not be written: it reads and searches, it writes nothing, and every write is refused with a `LuiReadOnlyException` | ADR-0033, issue #56 |
| [`EmbeddedIndexTests`](../tests/Nordmann.LUI.Integration.Tests/EmbeddedIndexTests.cs) | Integration | the schema and the write API of an embedded index: `Create()`, the writes, the payload (no part of the definition, a type the serialiser does not know, a catalog without a serialiser, read when it is asked for and not before, two hits of the same entry are equal), the transaction of the application | spec §7.2, §12.3, §12.4, ADR-0035 |
| [`LinkedIndexTests`](../tests/Nordmann.LUI.Integration.Tests/LinkedIndexTests.cs) | Integration | a linked index over the table of the application: the table is not touched, the sync, the refresh, the triggers, the outbox; a payload row that is kept throws; what is read live (the text, the payload) and what waits for a refresh (the weight) | spec §5, §7.3, ADR-0015, ADR-0035 |
| [`SyncCheckRebuildTests`](../tests/Nordmann.LUI.Integration.Tests/SyncCheckRebuildTests.cs) | Integration | sync, check, rebuild and optimize — against a source and against itself | spec §12.4, §12.6, §14, ADR-0016 |
| [`SingleWordQueryTests`](../tests/Nordmann.LUI.Integration.Tests/SingleWordQueryTests.cs) | Integration | the query of a single-word index: the prefix path, the trigram candidates, the ranking | spec §8, §9, §12.5 |
| [`MultiWordIndexTests`](../tests/Nordmann.LUI.Integration.Tests/MultiWordIndexTests.cs) | Integration | the multi-word index: the schema, the candidates, the ranking of names and titles — and Anhang B | spec §8.2, §9, ADR-0011 |
| [`GoldenQueryTests`](../tests/Nordmann.LUI.Integration.Tests/GoldenQueryTests.cs) | Integration | the golden queries as fixed expectations — the system tag first among 30,000 hits | spec §19, Anhang B |
| [`NormalizationParity`](../tests/Nordmann.LUI.Integration.Tests/NormalizationParity.cs) | Integration | the rule exists twice — as SQL and as C# — and both give the same answer, for every code point and for every word of the list | spec §6.2, §19, ADR-0005 |
| [`FoldProbeTests`](../tests/Nordmann.LUI.Integration.Tests/FoldProbeTests.cs) | Integration | the probe that asks the loaded SQLite how it folds — and that it finds a table which folds differently | ADR-0007 |
| [`QueryPlanTests`](../tests/Nordmann.LUI.Integration.Tests/QueryPlanTests.cs) | Integration | the plan of the candidate query: the search index in the rowid order, no sorter | spec §8.2, §19 |
| [`SchemaGoldenTests`](../tests/Nordmann.LUI.Integration.Tests/SchemaGoldenTests.cs) | Integration | the schema of an embedded index is the golden one — byte for byte | ADR-0014, ADR-0015 |
| [`TypoToleranceTests`](../tests/Nordmann.LUI.Integration.Tests/TypoToleranceTests.cs) | Integration | a typo does not lead nowhere: the four kinds of edit, the correction, the limits | spec §10, ADR-0012 |
| [`TypoDictionaryLifecycleTests`](../tests/Nordmann.LUI.Integration.Tests/TypoDictionaryLifecycleTests.cs) | Integration | a query does not wait for a build that runs and says so; a sync teaches the dictionary its new words, a removed word lingers until a rebuild; two indexes never correct towards each other's words | spec §10.1, §10.3a, ADR-0021, ADR-0025 |
| [`SampleApiTests`](../tests/Nordmann.LUI.Integration.Tests/SampleApiTests.cs) | Integration | the sample's API on a real Kestrel: every setting of a query is a parameter that reaches `LuiQueryOptions`; the three indexes (embedded + JSON payload, linked + column payload + priority, embedded without payload); a dictionary of its own per index; the write side and the maintenance; an abandoned request stops the server's work; the policy of the page | ADR-0027, PR-011 |
| [`SampleBulkTests`](../tests/Nordmann.LUI.Integration.Tests/SampleBulkTests.cs) | Integration | the bulk indexes of the sample: the button that creates an index and loads it, the progress of the load, and exactly the number of entries that were asked for — and nothing else; the bulk indexes are not registered at startup, and the tests point the sample at the curated word list — a test never downloads 5.5 MB | ADR-0029 |
| [`SampleIndexPanelTests`](../tests/Nordmann.LUI.Integration.Tests/SampleIndexPanelTests.cs) | Integration | the panel of the indexes: one index that cannot be read does not take the others down with it — an empty panel looks like data that was lost | ADR-0029 |
| [`TypoQualityTests`](../tests/Nordmann.LUI.Integration.Tests/TypoQualityTests.cs) | Integration | the quality of the correction against a measured floor, by the length of the beginning | spec §10.4, §19, Anhang A4 |
| [`ConcurrencyTests`](../tests/Nordmann.LUI.Integration.Tests/ConcurrencyTests.cs) | Integration | reads in parallel, writes serial — neither disturbs the other | spec §15 |
| [`ApiShapeTests`](../tests/Nordmann.LUI.Integration.Tests/ApiShapeTests.cs) | Integration | the shape of the API before 0.1.0: the entry, the batch, the options that check themselves, the read and the write interface, the options of an index description that every step keeps, the exceptions of the library that are caught as one | ADR-0013, ADR-0024 |
| [`RowIdTests`](../tests/Nordmann.LUI.Integration.Tests/RowIdTests.cs) | Integration | the rowid carries the priority and the weight — and the SQL and the C# agree on it | spec §7.2 |
| [`ReviewFollowUpTests`](../tests/Nordmann.LUI.Integration.Tests/ReviewFollowUpTests.cs) | Integration | the findings of the review of pull request 6, each reproduced red before it was fixed | review of PR #6 |
| [`TypoReviewTests`](../tests/Nordmann.LUI.Integration.Tests/TypoReviewTests.cs) | Integration | the findings of the review of pull request 10, each reproduced red before it was fixed | review of PR #10 |
| [`PerformanceBudget_Machine`](../tests/Nordmann.LUI.Integration.Tests/PerformanceBudget_Machine.cs) | Integration | the two kinds of target of §16: a time belongs to the machine it was measured on and is reported elsewhere; a size is the same everywhere and is asserted | spec §16, ADR-0041 |

### The measurements — they run on purpose

They are not tests of behaviour; they are the numbers in
[`docs/benchmarks.md`](./benchmarks.md). They are skipped unless `LUI_PERF=1`. The corpus they measure is
generated by `Corpus` in `Nordmann.LUI.Testing` — the same one the benchmark project
(`benchmarks/Nordmann.LUI.Benchmarks`) measures (ADR-0020).

The lane of the schedule runs them every night on a shared runner: there the **times** are reported — a time belongs to the
machine it was measured on — and the **sizes** are asserted (ADR-0041).

| Test file | Layer | Measures | The number |
|---|---|---|
| [`PerformanceBudgets`](../tests/Nordmann.LUI.Integration.Tests/PerformanceBudgets.cs) | Integration | **the seven targets of §16 in their scenario** — 3 M entries, the whole word list, 1 M titles; each one met and asserted, or waived with a reason | spec §16, ADR-0020 |
| [`QueryPerformance`](../tests/Nordmann.LUI.Integration.Tests/QueryPerformance.cs) | Integration | the latency and the allocation of a query, the cost of a write, the build | spec §16 |
| [`AllocationPoolTests`](../tests/Nordmann.LUI.Integration.Tests/AllocationPoolTests.cs) | Integration | the allocation of a query **after the candidate budget has changed** — the pool of the buffers must not fill up with the lengths of the past | spec §16, ADR-0031 |
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
| [`Public_API_Shape`](../tests/Nordmann.LUI.Architecture.Tests/Public_API_Shape.cs) | Architecture | the public API is a promise: no web stack, no `Task`, one namespace, one prefix, no positional constructors, the content sources are called `Embedded` and `Linked` (the retired word `Owned` is gone), every exception is a `LuiException`, every option of a query can be set on the query, the read interface only reads, the catalog creates and opens an index, the container holds the catalog and nothing else, and the retired names are gone; the index knows no types — no type parameter on the index, the hit, the result, the query, the entry or the builders, the payload a method of the hit, one serialiser per catalog | spec §13, ADR-0013, ADR-0023, ADR-0024, ADR-0033, ADR-0035 |
| [`Public_API_Documentation`](../tests/Nordmann.LUI.Architecture.Tests/Public_API_Documentation.cs) | Architecture | the public API has a page of its own: every public type of the two packages is on [`docs/api.md`](./api.md), the page names no type the code does not have, and the README names none either | ADR-0038 |
| [`Repository_Layout`](../tests/Nordmann.LUI.Architecture.Tests/Repository_Layout.cs) | Architecture | the build wiring is part of the contract: one place for the versions, exactly one packable project, the documented dependency set | ADR-0001, ADR-0002, ADR-0004, ADR-0034 |
| [`Namespace_Convention`](../tests/Nordmann.LUI.Architecture.Tests/Namespace_Convention.cs) | Architecture | folder = namespace, every namespace below the root | spec §17.2 |
| [`Readme_Benchmarks`](../tests/Nordmann.LUI.Architecture.Tests/Readme_Benchmarks.cs) | Architecture | the BenchmarkDotNet tables of the README are rendered from the committed reports of one run; every benchmark class has a report and a section | ADR-0022, ADR-0039 |
| [`Readme_Budgets`](../tests/Nordmann.LUI.Architecture.Tests/Readme_Budgets.cs) | Architecture | the budget table of the README is the promise of §16: the limits of the entrance, of the target table of the record and of its status table are read out of the specification and compared with it — a limit that drifts fails the build | issue #38 |
| [`Sample_Client`](../tests/Nordmann.LUI.Architecture.Tests/Sample_Client.cs) | Architecture | the sample's client: modules by concern with the direction of the imports as a table, one fetch boundary, no HTML from a string, no inline script or style, no dependency, and the vendored flexdatalist is the file its provenance names | ADR-0027 |
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

# the modules of the sample's client — plain Node, no install, no build (ADR-0027)
(cd samples/Lui.Sample && npm test)

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

---

## The sample's client

The page of the sample (`samples/Lui.Sample/wwwroot`) is plain ES modules; its tests are the ones Node runs on its own
(`samples/Lui.Sample/client-tests/*.test.js`, `npm test`, nothing to install). They are not rows of the map above — the map
lists C# files — but they are a lane of CI:

| Test file | Proves |
|---|---|
| `presenters.test.js` | for every scenario, with hostile data in every field: every displayed property has an escaped `_highlight` twin and no markup survives — flexdatalist writes the displayed values with `innerHTML` |
| `did-you-mean.test.js` | what counts as a correction worth a question: `correctedQuery` there and different from what was typed |
| `settings.test.js` | only what differs from the library's default is sent; the three-valued typo tolerance; the demo latency; the storage that is blocked, broken or holds rubbish |
| `transport.test.js` | the fetch boundary: a new request for an index aborts the one in flight, `cancel(index)` on a keystroke, the envelope on the bus, a refused request and a network error as an empty list |
| `bus.test.js`, `format.test.js` | the event bus; the formatting and `escapeHtml` |

What a browser alone shows — the CSS the library expects from the stylesheet, a policy that refuses an inline style, the
list under the input — was checked in Chromium for PR-011 (typo correction and "Did you mean", groups, the tag picker and add-new, abort while
typing fast, a hostile entry in the recipes and in the tags table); the run is described in the pull request.
