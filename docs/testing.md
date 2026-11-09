# The test landscape

> Which test proves what. The specification is the contract of the library; this
> file says which test holds which part of it up — so that a change to a rule can
> be answered in one minute, without reading thirty test files.

**The map is a guard, not a document.** The architecture test
[`TestLandscapeTests.cs`](../tests/Nordmann.LUI.Architecture.Tests/TestLandscapeTests.cs)
reads this file and the tree, and fails when they disagree: a test file that
is not listed here, a row that points at a file that does not exist, a layer
that does not match the project the file lives in. A map that rots is worse
than no map — it sends the reader to a test that no longer proves the rule.

---

## The three layers

The standards ask for a test pyramid: unit, architecture, integration. LUI has
three test projects, one per layer, plus the shared infrastructure they all
build on — and the sample application has a test project of its own, beside it.

| Project | What it holds | How long it takes | Needs a database |
|---|---|---|---|
| [`Nordmann.LUI.Unit.Tests`](../tests/Nordmann.LUI.Unit.Tests) | the rules of the library on their own — the normalisation, the word start, the similarity, the FTS5 expression | **~1 s** | no |
| [`Nordmann.LUI.Integration.Tests`](../tests/Nordmann.LUI.Integration.Tests) | the library against a real SQLite file — the schema, the writes, the query, the sync | **~32 s** | yes, one file per test |
| [`Nordmann.LUI.Architecture.Tests`](../tests/Nordmann.LUI.Architecture.Tests) | the shape of the repository and of the public API | **~0.5 s** | no |
| [`Lui.Sample.Tests`](../samples/Lui.Sample.Tests) | the sample application against its API, on a real Kestrel — beside the sample, not among the tests of the library | **~10 s** | yes, one file per test class |
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
| `LUI_PERF=1` | the measurements | a person, on purpose: [`docs/benchmarks.md`](./benchmarks.md) |
| `LUI_PERF_ENTRIES` | how many entries a measurement builds (default 200,000) | the same |
| `LUI_PERF_KIND=multi` | whether a measurement runs on the multi-word index | the same |
| `LUI_REQUIRE_FULL_WORDLIST=1` | the full 685,829-word parity **must** run, or the test fails | the lane of the parity in [`ci.yml`](../.github/workflows/ci.yml) — on every pull request, and in the release, which calls `ci.yml` |
| `LUI_WORDLIST` | where the full word list is | a person, on purpose |
| `LUI_PERF_MACHINE=other` | the machine of the run: `measurement` asserts every target, `other` reports the times and asserts the sizes | the lane (`.github/workflows/performance.yml`) |

**A skipped test that reports itself as passed proves nothing.** The full-word-list
parity is skipped — and says so — when the list is missing, rather than
returning green. The lane of the parity sets `LUI_REQUIRE_FULL_WORDLIST=1` — on every pull request, and in the release, which calls it — and then
the test fails with the reason instead of skipping.

### The lanes of the CI

The lanes of [`ci.yml`](../.github/workflows/ci.yml) are the map above, spread over the runners — and the lane of the
schedule is a lane of its own ([`performance.yml`](../.github/workflows/performance.yml)). Every job sets itself up the
same way ([`.github/actions/setup`](../.github/actions/setup/action.yml): the SDK of [`global.json`](../global.json), the
cache of the packages, the restore), runs on a named image, has a timeout, and stops a test that hangs for ten minutes
with a full dump that is uploaded with the results (ADR-0047, guarded by `WorkflowRulesTests`).

| Lane | What it runs | What it is there for |
|---|---|---|
| Workflows · actionlint | `actionlint` over every workflow (the release, the budgets, this one) | `publish.yml` runs only on a tag — a mistake in it is found on the pull request, not at the release (ADR-0048) |
| Format | `dotnet format --verify-no-changes` | on its own and first: a format error is reported in a minute, not after every test |
| Build · Test | the three layers, the tests of the sample, the client of the sample | the whole tree, on every pull request |
| The normalisation parity over the whole word list | the one test of `NormalizationParityTests` that needs the full list (685,829 words) | the rule of §6.2 exists twice — as SQL and as C#; the rest of the layer runs in *Build · Test*, nothing twice |
| The guards bite | the mutation check, in four shards side by side (`--shard i/4`), and one job of that name that waits for all four | a green test proves nothing until it has been seen red (ADR-0019) — and every mutation says how it must be caught: by its test (`expect_caught`) or, where the toolchain is the guard on purpose, by the build with a named error (`expect_refused`). A mutation that the build refuses although a test was meant fails the check (issue #73) |
| Package · Smoke test | the packed library in a throwaway application, beside the other jobs | the README's code is part of the test |
| **Windows** | the three layers and the tests of the sample on `windows-2025` | the paths, the pragmas and the case of the file names — the platform where a path is a promise (issue #67) |
| **Publish** (a tag `v*`) | the tag against `Directory.Build.props` and `main`, then **every lane above** (it calls `ci.yml`), then — after the owner's approval — the push with a key of one hour (Trusted Publishing) | a release passes exactly the gates of a pull request, and stores no key (ADR-0048) |
| Performance (every night, by hand) | the seven targets of §16 | a time belongs to the machine it was measured on; a size is the same everywhere (ADR-0041) |

Dependabot ([`.github/dependabot.yml`](../.github/dependabot.yml)) moves the pins — the actions, the packages, the SDK —
weekly, each as a pull request through every lane above.

---

## Unit — the rules on their own

The folders are the subjects of the library (`Text/`, `Similarity/`, …), and the folder of a file is its namespace —
as in the library (`NamespaceConventionTests`). `Support/` holds the types the tests of several subjects share.

### Text

| Test file | Layer | Proves | The rule |
|---|---|---|
| [`NormalizerTests`](../tests/Nordmann.LUI.Unit.Tests/Text/NormalizerTests.cs) | Unit | the search form and the compact form of a text; the German transliteration, on and off; the separators | spec §6.1, ADR-0005 |
| [`WordStartTests`](../tests/Nordmann.LUI.Unit.Tests/Text/WordStartTests.cs) | Unit | the word-start table — every row of it, and the hashtag and the hyphen | spec §9.1 |
| [`SearchFormParityTests`](../tests/Nordmann.LUI.Unit.Tests/Text/SearchFormParityTests.cs) | Unit | the search form in the buffer and as a string: the same form — the ranking measures in the buffer | issue #30, ADR-0032 |

### Similarity

| Test file | Layer | Proves | The rule |
|---|---|---|
| [`SimilarityTests`](../tests/Nordmann.LUI.Unit.Tests/Similarity/SimilarityTests.cs) | Unit | the four measures: identical texts, unrelated texts, the range, the thread safety | spec §11 |
| [`JaroWinklerEquivalenceTests`](../tests/Nordmann.LUI.Unit.Tests/Similarity/JaroWinklerEquivalenceTests.cs) | Unit | LUI's own Jaro-Winkler against the library it replaces: the same number for known pairs, 10,000 random pairs, the lengths 0 … 12 — without a tolerance | issue #30, ADR-0032 |
| [`DamerauEquivalenceTests`](../tests/Nordmann.LUI.Unit.Tests/Similarity/DamerauEquivalenceTests.cs) | Unit | LUI's own Damerau-Levenshtein against the library it replaces: the same number, without a tolerance, and no allocation | issue #30, ADR-0030 |
| [`LevenshteinEquivalenceTests`](../tests/Nordmann.LUI.Unit.Tests/Similarity/LevenshteinEquivalenceTests.cs) | Unit | LUI's own Levenshtein against the library it replaces: the same number, without a tolerance, and no allocation | issue #30, ADR-0030 |
| [`NgramDiceEquivalenceTests`](../tests/Nordmann.LUI.Unit.Tests/Similarity/NgramDiceEquivalenceTests.cs) | Unit | LUI's own N-gram Dice gives the same number as the library it replaces, without allocating | ADR-0011 |

### Keys, payload, querying, typos, package

| Test file | Layer | Proves | The rule |
|---|---|---|
| [`KeyCodecTests`](../tests/Nordmann.LUI.Unit.Tests/Keys/KeyCodecTests.cs) | Unit | the key between the application and the file: a key of the other kind refused with the index and both kinds, the stored text, the names of the kinds the files already carry, a name of a kind this version does not know | ADR-0035 |
| [`LuiKeyTests`](../tests/Nordmann.LUI.Unit.Tests/Keys/LuiKeyTests.cs) | Unit | the key: a number or a text without a cast, back with one; `1` is not `"1"`; the conversions out of it throw for the other kind | ADR-0035 |
| [`LuiPayloadSerializerTests`](../tests/Nordmann.LUI.Unit.Tests/Payload/LuiPayloadSerializerTests.cs) | Unit | the JSON serialiser of the catalog: the round trip, the UTF-8, the type of the instance, a type missing from the context named on the write and the read, the strict reading (a type of another shape refused, a missing constructor parameter refused, a type with every member and optional ones besides read), the refused null | ADR-0013, ADR-0035 |
| [`MultiWordExpressionTests`](../tests/Nordmann.LUI.Unit.Tests/Querying/MultiWordExpressionTests.cs) | Unit | the FTS5 expression: the words ANDed, the quoting, the input never copied into the expression raw | spec §8.2 |
| [`RankingTests`](../tests/Nordmann.LUI.Unit.Tests/Querying/RankingTests.cs) | Unit | the ranking in groups, and the lazy similarity — the promise that a group that cannot reach the hits costs nothing | ADR-0008 |
| [`TypoDictionaryStateTests`](../tests/Nordmann.LUI.Unit.Tests/Typos/TypoDictionaryStateTests.cs) | Unit | the states of the typo dictionary — not built, being built, current —, that it only grows, that every index has its own, and that a query's build does not wait behind another one | ADR-0021, ADR-0025 |
| [`PackageIdentityTests`](../tests/Nordmann.LUI.Unit.Tests/Package/PackageIdentityTests.cs) | Unit | the package identity and the dependency set of the compiled assembly | ADR-0004 |

## Integration — the library against SQLite

Sorted by subject, a folder per subject — and the folder is the namespace. A test lives with the behaviour it proves,
not with the review that found it: where a test came from is a comment above it (`// review of pull request 6, B1: …`).
`Support/` holds the attributes that gate a test on its input; `Performance/` holds the measurements and what they need.

### Catalog

| Test file | Layer | Proves | The rule |
|---|---|---|
| [`CatalogTests`](../tests/Nordmann.LUI.Integration.Tests/Catalog/CatalogTests.cs) | Integration | opening a catalog, the meta table, the journal mode (WAL by default, or the one the database has), the SQLite version, the names, the lifetime, the clock, the disposal while a query runs; the options of the catalog that check themselves and reach the connections; the exceptions of the library that are caught as one; a dropped index | spec §12.1, §12.8, ADR-0013, ADR-0024, review of PR #6 |
| [`IndexBuilderTests`](../tests/Nordmann.LUI.Integration.Tests/Catalog/IndexBuilderTests.cs) | Integration | the description of an index: a kind of key that is not defined fails where it is chosen, a payload keeps the settings made before it, every step keeps every setting | ADR-0013, ADR-0033, #40 |
| [`ReadPathTests`](../tests/Nordmann.LUI.Integration.Tests/Catalog/ReadPathTests.cs) | Integration | the read path: an index that is opened and not created — the index that is not there, a foreign schema version, a foreign folding, nothing written, no write lock, a read-only file, the kind of the key from the file and a key of the other kind, the payload read by the serialiser of the catalog that opens the file, a payload that cannot be read as the type asked for, a payload read as a type of another shape, whatever a serialiser of the application throws, the definition of an older version and of an unknown key type, an index opened as what it is, a linked index with a payload | ADR-0033, ADR-0035, issues #56, #58 |
| [`ReadOnlyCatalogTests`](../tests/Nordmann.LUI.Integration.Tests/Catalog/ReadOnlyCatalogTests.cs) | Integration | the catalog on a file that may not be written: it reads and searches, it writes nothing, and every write is refused with a `LuiReadOnlyException` | ADR-0033, issue #56 |

### Embedded

| Test file | Layer | Proves | The rule |
|---|---|---|
| [`EmbeddedIndexTests`](../tests/Nordmann.LUI.Integration.Tests/Embedded/EmbeddedIndexTests.cs) | Integration | the schema and the definition of an embedded index: what `Create()` makes, the definition in the meta table, an index that is not the one asked for (another definition, another key, another fold table), the drop, the independence of two indexes, a new catalog on the same file | spec §7.2, §7.4, §12.2 |
| [`EmbeddedWriteTests`](../tests/Nordmann.LUI.Integration.Tests/Embedded/EmbeddedWriteTests.cs) | Integration | the writes: the entry that checks its values, the upsert that replaces the whole entry, the updates, the removal, the transaction of the application and the refusal of another database's, the end of the document ids; a write that changes nothing leaves the search index alone | spec §7.2, §12.3, §12.4, ADR-0013, review of PR #6 |
| [`EmbeddedPayloadTests`](../tests/Nordmann.LUI.Integration.Tests/Embedded/EmbeddedPayloadTests.cs) | Integration | the payload: a type the serialiser does not know, a catalog without a serialiser, read when it is asked for and not before, two hits of the same entry are equal, a value type as a payload | spec §12.8, ADR-0035, review of PR #6 |
| [`EmbeddedBatchTests`](../tests/Nordmann.LUI.Integration.Tests/Embedded/EmbeddedBatchTests.cs) | Integration | the batch: every entry written and counted, the last of a key that comes twice, all or nothing in its own transaction, a cancelled batch, the transaction of the application, the typo dictionary taught as by a single upsert, a batch beside the writes of other indexes | spec §12.4, ADR-0013 |
| [`SchemaGoldenTests`](../tests/Nordmann.LUI.Integration.Tests/Embedded/SchemaGoldenTests.cs) | Integration | the schema of an embedded index is the golden one — byte for byte | ADR-0014, ADR-0015 |
| [`RowIdTests`](../tests/Nordmann.LUI.Integration.Tests/Embedded/RowIdTests.cs) | Integration | the rowid carries the priority and the weight — and the SQL and the C# agree on it | spec §7.2 |

### Linked

The four files share one table of the application ([`LinkedTable`](../tests/Nordmann.LUI.Integration.Tests/Linked/LinkedTable.cs)).

| Test file | Layer | Proves | The rule |
|---|---|---|
| [`LinkedIndexTests`](../tests/Nordmann.LUI.Integration.Tests/Linked/LinkedIndexTests.cs) | Integration | a linked index over the table of the application: the table is not touched, the text is not stored twice, a table that is not what the link says, the names, the drop, another column is another definition, a column renamed under the index, the way out of a mismatch, the keys are the numbers of the table | spec §5, §7.3, ADR-0015 |
| [`LinkedSyncTests`](../tests/Nordmann.LUI.Integration.Tests/Linked/LinkedSyncTests.cs) | Integration | the application keeps the index current: the sync, the refresh with and without the transaction of the application, many rows at once, the weight and the priority from their columns | spec §12.6, ADR-0015 |
| [`LinkedTriggerTests`](../tests/Nordmann.LUI.Integration.Tests/Linked/LinkedTriggerTests.cs) | Integration | the triggers, only on request: the script, the outbox they write and nothing else, a refresh and a sync take their rows out of it, the drop that refuses to leave the triggers behind, the state of the triggers | ADR-0015 |
| [`LinkedQueryTests`](../tests/Nordmann.LUI.Integration.Tests/Linked/LinkedQueryTests.cs) | Integration | what comes back: the payload read from the columns, a payload row that is kept throws, the keys at the end of the range, the multi-word index over a table, the same answer as an embedded index, the typo dictionary from the table; what is read live (the text, the payload) and what waits for a refresh (the weight); the stale candidates and the budget | ADR-0015, ADR-0035, review of PR #16 |

### Querying

| Test file | Layer | Proves | The rule |
|---|---|---|
| [`SingleWordQueryTests`](../tests/Nordmann.LUI.Integration.Tests/Querying/SingleWordQueryTests.cs) | Integration | the query of a single-word index: the prefix path, the trigram candidates, the ranking, the options and their ranges, the defaults, the read interface; the snapshot of the candidates and the hits, the hash, the code points, the cancellation while ranking | spec §8, §9, §12.5, review of PR #6 |
| [`MultiWordIndexTests`](../tests/Nordmann.LUI.Integration.Tests/Querying/MultiWordIndexTests.cs) | Integration | the multi-word index: the schema, the candidates, the ranking of names and titles — and Anhang B | spec §8.2, §9, ADR-0011 |
| [`GoldenQueryTests`](../tests/Nordmann.LUI.Integration.Tests/Querying/GoldenQueryTests.cs) | Integration | the golden queries as fixed expectations — the system tag first among 30,000 hits | spec §19, Anhang B |
| [`QueryPlanTests`](../tests/Nordmann.LUI.Integration.Tests/Querying/QueryPlanTests.cs) | Integration | the plan of the candidate query: the search index in the rowid order, no sorter | spec §8.2, §19 |
| [`AllocationPoolTests`](../tests/Nordmann.LUI.Integration.Tests/Querying/AllocationPoolTests.cs) | Integration | the allocation of a query **after the candidate budget has changed** — the pool of the buffers must not fill up with the lengths of the past | spec §16, ADR-0031 |
| [`TypedIndexTests`](../tests/Nordmann.LUI.Integration.Tests/Querying/TypedIndexTests.cs) | Integration | the typed view of an index: the payload is read in the type of the application (a method, `GetPayload()`, not a property), the view answers as the index answers, and the fluent path ends in the same typed answer (`LuiResult.As<T>()`) | issue #59, ADR-0044 |

### Typos

| Test file | Layer | Proves | The rule |
|---|---|---|
| [`TypoToleranceTests`](../tests/Nordmann.LUI.Integration.Tests/Typos/TypoToleranceTests.cs) | Integration | a typo does not lead nowhere: the four kinds of edit, the correction, the limits; the cap keeps the candidates of the correction, `DistinctText` keeps the correction, the builder takes a bool | spec §10, ADR-0012, review of PR #10 |
| [`TypoDictionaryLifecycleTests`](../tests/Nordmann.LUI.Integration.Tests/Typos/TypoDictionaryLifecycleTests.cs) | Integration | a query does not wait for a build that runs and says so; the build does not hold up the writers, and a rebuild loses nothing; a sync teaches the dictionary its new words, a removed word lingers until a rebuild, and a rolled-back write too; one dictionary per index — two instances share it, two indexes never correct towards each other's words, a dropped index leaves none behind | spec §10.1, §10.3a, ADR-0021, ADR-0025, review of PR #10 |
| [`TypoQualityTests`](../tests/Nordmann.LUI.Integration.Tests/Typos/TypoQualityTests.cs) | Integration | the quality of the correction against a measured floor, by the length of the beginning | spec §10.4, §19, Anhang A4 |

### Maintenance, concurrency, hosting, normalisation

| Test file | Layer | Proves | The rule |
|---|---|---|
| [`SyncCheckRebuildTests`](../tests/Nordmann.LUI.Integration.Tests/Maintenance/SyncCheckRebuildTests.cs) | Integration | sync, check, rebuild and optimize — against a source and against itself | spec §12.4, §12.6, §14, ADR-0016 |
| [`ConcurrencyTests`](../tests/Nordmann.LUI.Integration.Tests/Concurrency/ConcurrencyTests.cs) | Integration | reads in parallel, writes serial — neither disturbs the other; the soak of eight readers and one writer keeps the count; a second catalog on the same file sees a consistent picture while the first one writes; a dispose during a warm-up ends and does not hang | spec §15, issue #67 |
| [`BusyTimeoutTests`](../tests/Nordmann.LUI.Integration.Tests/Concurrency/BusyTimeoutTests.cs) | Integration | the busy timeout of the catalog is the wait for a lock — 1 s and 2 s wait at least what they say, and seconds, not the 30 s of the provider — and a statement that runs longer than a second is not interrupted | issue #70, ADR-0046, ADR-0050 |
| [`DependencyInjectionTests`](../tests/Nordmann.LUI.Integration.Tests/Hosting/DependencyInjectionTests.cs) | Integration | LUI in a host: the catalog is the one service of the container, no index is registered, the settings of the container are its own, the catalog goes with the host | spec §12.7, ADR-0017, ADR-0018, ADR-0033 |
| [`LuiWarmupTests`](../tests/Nordmann.LUI.Integration.Tests/Hosting/LuiWarmupTests.cs) | Integration | the warm-up as a service of the host: the dictionaries are warmed at the start, the outbox of a linked index is applied, the rounds run on the interval, and without the registration nothing happens; only the named dictionaries are built, the outbox runs without them, an unknown name fails the start, a failed round reaches the logger of the host (with `RecordingLoggerProvider`, the logger that keeps what it heard), and the registration is once and with a positive interval | issue #68, ADR-0043, ADR-0045 |
| [`NormalizationParityTests`](../tests/Nordmann.LUI.Integration.Tests/Normalization/NormalizationParityTests.cs) | Integration | the rule exists twice — as SQL and as C# — and both give the same answer, for every code point and for every word of the list | spec §6.2, §19, ADR-0005 |
| [`FoldProbeTests`](../tests/Nordmann.LUI.Integration.Tests/Normalization/FoldProbeTests.cs) | Integration | the probe that asks the loaded SQLite how it folds — and that it finds a table which folds differently | ADR-0007 |
| [`BudgetMachineTests`](../tests/Nordmann.LUI.Integration.Tests/Performance/BudgetMachineTests.cs) | Integration | the two kinds of target of §16: a time belongs to the machine it was measured on and is reported elsewhere; a size is the same everywhere and is asserted | spec §16, ADR-0041 |

### The measurements — they run on purpose
They are not tests of behaviour; they are the numbers in
[`docs/benchmarks.md`](./benchmarks.md). They are skipped unless `LUI_PERF=1`. The corpus they measure is
generated by `Corpus` in `Nordmann.LUI.Testing` — the same one the benchmark project
(`benchmarks/Nordmann.LUI.Benchmarks`) measures (ADR-0020).

The lane of the schedule runs them every night on a shared runner: there the **times** are reported — a time belongs to the
machine it was measured on — and the **sizes** are asserted (ADR-0041).

| Test file | Layer | Measures | The number |
|---|---|---|
| [`BudgetPerformance`](../tests/Nordmann.LUI.Integration.Tests/Performance/BudgetPerformance.cs) | Integration | **the seven targets of §16 in their scenario** — 3 M entries, the whole word list, 1 M titles; each one met and asserted, or waived with a reason | spec §16, ADR-0020 |
| [`QueryPerformance`](../tests/Nordmann.LUI.Integration.Tests/Performance/QueryPerformance.cs) | Integration | the latency and the allocation of a query, the cost of a write, the build | spec §16 |
| [`SyncPerformance`](../tests/Nordmann.LUI.Integration.Tests/Performance/SyncPerformance.cs) | Integration | what a sync costs, a cancelled one, the check, the rebuild — and a delta sync beside a built typo dictionary | spec §20 step 7, ADR-0016, ADR-0021 |
| [`TypoPerformance`](../tests/Nordmann.LUI.Integration.Tests/Performance/TypoPerformance.cs) | Integration | what the typo dictionary costs, and how good the correction is on the whole list | spec §16, ADR-0012 |
| [`LinkedPerformance`](../tests/Nordmann.LUI.Integration.Tests/Performance/LinkedPerformance.cs) | Integration | what a linked index costs against the table of the application | ADR-0015 |
| [`BatchPerformance`](../tests/Nordmann.LUI.Integration.Tests/Performance/BatchPerformance.cs) | Integration | what a batch saves over a loop of single upserts | ADR-0013 |

## Sample — the sample application against its API

The tests of the sample sit beside it, in [`samples/Lui.Sample.Tests`](../samples/Lui.Sample.Tests) (issue #76): the
library's tests do not reference the sample, and a change of the sample does not rebuild them. They host the sample on
a real Kestrel (`SampleHost`) over a database file of their own.

| Test file | Layer | Proves | The rule |
|---|---|---|
| [`SampleApiTests`](../samples/Lui.Sample.Tests/SampleApiTests.cs) | Sample | the sample's API on a real Kestrel: every setting of a query is a parameter that reaches `LuiQueryOptions`; the three indexes (embedded + JSON payload, linked + column payload + priority, embedded without payload); a dictionary of its own per index; the write side and the maintenance; an abandoned request stops the server's work (abandoned once the server has it, issue #94); the policy of the page | ADR-0027, PR-011 |
| [`SampleBulkTests`](../samples/Lui.Sample.Tests/SampleBulkTests.cs) | Sample | the bulk indexes of the sample: the button that creates an index and loads it, the progress of the load, and exactly the number of entries that were asked for — and nothing else; the bulk indexes are not registered at startup, and the tests point the sample at the curated word list — a test never downloads 5.5 MB | ADR-0029 |
| [`SampleIndexPanelTests`](../samples/Lui.Sample.Tests/SampleIndexPanelTests.cs) | Sample | the panel of the indexes: one index that cannot be read does not take the others down with it — an empty panel looks like data that was lost | ADR-0029 |

## Architecture — the shape of the repository

One folder: the rules read the tree, not a subject of the library. The names carry the suffix of every other test class
(`…Tests`), so that a filter, the test explorer and the mutation check speak one language (issue #77).

| Test file | Layer | Proves | The rule |
|---|---|---|
| [`InternalLayeringTests`](../tests/Nordmann.LUI.Architecture.Tests/InternalLayeringTests.cs) | Architecture | the inside of the library has a direction: a namespace knows only the ones below it, and no layer reaches up through the facade | ADR-0014 |
| [`PublicApiShapeTests`](../tests/Nordmann.LUI.Architecture.Tests/PublicApiShapeTests.cs) | Architecture | the public API is a promise: no web stack, no `Task`, one namespace, one prefix, no positional constructors, the content sources are called `Embedded` and `Linked` (the retired word `Owned` is gone), every exception is a `LuiException`, every option of a query can be set on the query, the read interface only reads, the catalog creates and opens an index, the container holds the catalog and nothing else, and the retired names are gone; the index knows no types — no type parameter on the index, the hit, the result, the query, the entry or the builders, the payload a method of the hit, one serialiser per catalog | spec §13, ADR-0013, ADR-0023, ADR-0024, ADR-0033, ADR-0035 |
| [`LinkCheckTests`](../tests/Nordmann.LUI.Architecture.Tests/LinkCheckTests.cs) | Architecture | every relative link of every markdown file points at something that exists — a link that points at nothing is a broken promise | issue #66, review of 2026-11-06, D |
| [`PublicApiDocumentationTests`](../tests/Nordmann.LUI.Architecture.Tests/PublicApiDocumentationTests.cs) | Architecture | the public API has a page of its own: every public type of the two packages is on [`docs/api.md`](./api.md), the page names no type the code does not have, and the README names none either | ADR-0038 |
| [`RepositoryLayoutTests`](../tests/Nordmann.LUI.Architecture.Tests/RepositoryLayoutTests.cs) | Architecture | the build wiring is part of the contract: one place for the versions, exactly one packable project, the documented dependency set | ADR-0001, ADR-0002, ADR-0004, ADR-0034 |
| [`NamespaceConventionTests`](../tests/Nordmann.LUI.Architecture.Tests/NamespaceConventionTests.cs) | Architecture | folder = namespace — in the library and in the tests —, every namespace below the root | spec §17.2, issue #77 |
| [`ReadmeBenchmarksTests`](../tests/Nordmann.LUI.Architecture.Tests/ReadmeBenchmarksTests.cs) | Architecture | the BenchmarkDotNet tables of the README are rendered from the committed reports of one run; every benchmark class has a report and a section | ADR-0022, ADR-0039 |
| [`ReadmeBudgetsTests`](../tests/Nordmann.LUI.Architecture.Tests/ReadmeBudgetsTests.cs) | Architecture | the budget table of the README is the promise of §16: the limits of the entrance, of the target table of the record and of its status table are read out of the specification and compared with it — a limit that drifts fails the build | issue #38 |
| [`SampleClientTests`](../tests/Nordmann.LUI.Architecture.Tests/SampleClientTests.cs) | Architecture | the sample's client: modules by concern with the direction of the imports as a table, one fetch boundary, no HTML from a string, no inline script or style, no dependency, and the vendored flexdatalist is the file its provenance names | ADR-0027 |
| [`WorkflowRulesTests`](../tests/Nordmann.LUI.Architecture.Tests/WorkflowRulesTests.cs) | Architecture | the pipelines change only with the repository: every action pinned by its commit with its version, every image named, every job with a timeout, every test run with a hang timeout and a full dump, every job that tests uploads its results, the release calls the gates of every pull request and pushes after them, in the environment of the owner, without a stored key, the SDK only from `global.json` — exact, in its feature band, read by the setup action (a roll-forward across bands and a `10.0.x` pin nothing), every `curl` retries a transient error and says who answered (read as commands, not lines) — over `*.yml` and `*.yaml` | ADR-0047, issues #74, #91, review of pull request 83 |
| [`TestLandscapeTests`](../tests/Nordmann.LUI.Architecture.Tests/TestLandscapeTests.cs) | Architecture | this map is the truth: every test file is listed here, every row points at a file that exists and names the layer of its project, every test project of the tree has a layer; the unit layer opens no database | this file, ADR-0019, issue #89 |
| [`TimingTraitTests`](../tests/Nordmann.LUI.Architecture.Tests/TimingTraitTests.cs) | Architecture | a test whose verdict depends on the clock — it waits for a time or reads one (`Stopwatch`, `DateTime.UtcNow`, `Environment.TickCount`, `Task.Delay`, `Thread.Sleep`, a `CancellationTokenSource` after a time, `WaitUntil`), in its body or in a helper of its file it calls — carries the trait `Category=Timing`, on the test or on its class, in any spelling xUnit takes | issue #78, ADR-0050, review of pull request 95 |

---

## The shared infrastructure

[`Nordmann.LUI.Testing`](../tests/Nordmann.LUI.Testing) holds what the tests
share. It has no tests of its own — it is the setup, in one place.

| Type | What it is for |
|---|---|
| [`TestDatabase`](../tests/Nordmann.LUI.Testing/TestDatabase.cs) | a database file of its own for one test, deleted afterwards. A file and not `:memory:` — every connection of the pool must see the same database. |
| [`Corpus`](../tests/Nordmann.LUI.Testing/Corpus.cs) | the generated corpus of the measurements: single words (the word list, then compounds) or titles, weights with a long tail or sparse, the inputs users type and the typos — the same for a seed, for the budget tests and the benchmarks alike (ADR-0020) |
| [`TestFiles`](../tests/Nordmann.LUI.Testing/TestFiles.cs) | where the corpora are: the sample word list, the edge cases, the full word list, the golden schema. One place, because eight tests each built their own path out of the repository root — and a path that is spelled eight times is wrong once. |
| [`ApplicationTable`](../tests/Nordmann.LUI.Testing/ApplicationTable.cs) | the table of the application, as a linked index sees it |
| [`RecipeStock`](../tests/Nordmann.LUI.Testing/RecipeStock.cs) | the stock of recipe titles the multi-word tests ask for |
| [`Chip`](../tests/Nordmann.LUI.Testing/Chip.cs) | the payload the integration tests attach to an entry. The unit layer has one of its own, so that it does not depend on the infrastructure that opens databases |
| [`TypoMaker`](../tests/Nordmann.LUI.Testing/TypoMaker.cs) | makes the typos the tests type |
| [`Repository`](../tests/Nordmann.LUI.Testing/Repository.cs) | finds the repository on disk — the architecture rules read the *source*, not a hand-written list |

## The test data

[`TestData`](../tests/TestData) holds the corpora —
no project, no code, just the files the tests read.

| File | What it is |
|---|---|
| [`wordlist/german-words.sample.txt`](../tests/TestData/wordlist/german-words.sample.txt) | every 97th word of the German word list — 7,071 words, deterministic, checked in |
| [`wordlist/edge-cases.txt`](../tests/TestData/wordlist/edge-cases.txt) | the characters a stride cannot reach for: umlauts, ß, accents, ligatures, decomposed forms |
| [`golden/embedded-schema.txt`](../tests/TestData/golden/embedded-schema.txt) | the `sqlite_master` of an embedded index, as it was before the second content source |

The full list (685,829 words) is **not** checked in — 5.5 MB that nobody
reviews. [`tools/fetch-wordlist.sh`](../tools/fetch-wordlist.sh) puts it in
`artifacts/wordlist/`, and the lane of the parity fetches it and demands it — on every pull request and in the release.

## How to run what

```bash
# the fast layer — the rules, without a database
dotnet test tests/Nordmann.LUI.Unit.Tests

# the layer against SQLite
dotnet test tests/Nordmann.LUI.Integration.Tests

# one subject — a folder is a namespace (ADR-0049)
dotnet test tests/Nordmann.LUI.Integration.Tests --filter "FullyQualifiedName~Integration.Tests.Linked"

# the shape of the repository
dotnet test tests/Nordmann.LUI.Architecture.Tests

# everything, as CI runs it
dotnet test Lui.sln

# the fast local loop: everything but the tests that read the clock (ADR-0050) — CI runs those too
dotnet test Lui.sln --filter "Category!=Timing"

# the modules of the sample's client — plain Node, no install, no build (ADR-0027)
(cd samples/Lui.Sample && npm test)

# the mutation check — all of it, one shard of it, or the one mutation you are writing
bash tools/mutation-check.sh
bash tools/mutation-check.sh --shard 2/4
bash tools/mutation-check.sh --only the-provider-retries-for-thirty-seconds

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
| A test asserts the order of magnitude of a time, never its milliseconds: a lower bound is strict (load only makes a wait longer), an upper bound has room and says why in the code. A test whose verdict depends on the clock carries `[Trait("Category", "Timing")]` | ADR-0050, guarded by `TimingTraitTests` |
| A test waits for the event it needs, not for a fixed window: a deadline only stops a hang | ADR-0050, issue #94 |

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
