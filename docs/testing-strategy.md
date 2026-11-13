# The testing strategy of LUI

> Why the tests of this repository look the way they do — for a developer who knows .NET testing and wants to write a
> test that belongs here.
>
> The [test map](./testing.md) says **which** test holds which rule up. This page says **why** the tests are built
> this way: the layers, the techniques and their reason, what is deliberately not done, and what a new test brings with
> it.

## The layers

Three layers of tests and a layer of measurements beside them, plus the tests of the sample application.

| Layer | Project | Needs a database | How long it takes |
|---|---|---|---|
| Unit | `tests/Nordmann.LUI.Unit.Tests` | no | seconds |
| Architecture | `tests/Nordmann.LUI.Architecture.Tests` | no | seconds |
| Integration | `tests/Nordmann.LUI.Integration.Tests` | yes — one file per test | a minute |
| Measurement | `tests/Nordmann.LUI.Performance.Tests` | yes | minutes — only when somebody asks for it |
| Sample | `samples/Lui.Sample.Tests` | yes | seconds |

The **unit layer** is the fast one, and it is where a rule is proved: a rule and its table, no file, no connection, no
schema. A unit test that names a database fails the build — it belongs to the integration layer.

The **architecture layer** reads the repository from disk: the shape of the public API, the folders of the tests, the
links of the documentation, the workflows. It is the mechanical half of the rules of the repository, and it is green in
seconds.

The **integration layer** is the only one that opens a database. It is the slowest, and it is the layer where the
behaviour of the library is actually visible.

The **measurement layer** stands beside the pyramid, not in it: a measurement asserts a number about a machine, a test
asserts a rule. The CI does not run it — the lane of the schedule does, once a week, and a developer runs it on purpose.

A **folder is a subject and a folder is a namespace** — in the tests as in the library (ADR-0049). `Text/`,
`Similarity/`, `Typos/` … the same subjects as the library, so a reader who knows the library knows the tree.

## The infrastructure and the test data

`tests/Nordmann.LUI.Testing` holds what the layers share. It has no tests of its own.

| The helper | What it is for |
|---|---|
| `TestDatabase` | a database file of its own for one test, deleted afterwards |
| `Repository` | the root of the repository on disk, the files of it, and their text with the line endings of the checkout normalised back |
| `TestFiles` | the paths of the test data: the sample word list, the edge cases, the whole word list, the golden schema |
| `Corpus`, `CorpusFile` | the corpora of the repository and the format of their files |
| `LuiBudgets`, `PerformanceBudget`, `BudgetMachine` | the budgets of the measurement, out of the file of ADR-0056 |
| `FullWordListFactAttribute`, `WordListGate` | the gate of the whole word list |
| `ApplicationTable`, `RecipeStock`, `Chip`, `RecipeCard` | the table of the application and the data of the sample |

The **test data** is small, fixed and in the repository:

- **The corpora** — four files in `tests/TestData/corpora/`, compressed, each with a header that names its source, its
  licence, its snapshot and its format. The file name is the identity of the corpus, and the corpus is not generated at
  the time of the test (ADR-0059).
- **The budgets** — one file, `tests/Nordmann.LUI.Testing/Budgets/budgets.json`: every target of the measurement with its
  limit, its unit and its kind (ADR-0056). The measured value is not in it — it belongs in the report, beside the
  machine it was measured on.
- **The scenario of a measurement** — `tests/Nordmann.LUI.Performance.Tests/performance.scenario.json`: the corpus, the
  prefix length, the settings of the run. It is checked in, so a run without the lane works too (ADR-0058).

One thing is **not** in the repository: the German word list of 685,829 words. It is fetched by
[`tools/fetch-wordlist.sh`](../tools/fetch-wordlist.sh) into `artifacts/wordlist/`. The guard that the list is really
there moved to the fetch: the script counts the lines and fails loudly, so that no lane can run the parity over a short
list and call it a proof (ADR-0058, D19).

## The techniques, and their reason

### Real SQLite, no mocks

The library is a **search engine on SQLite**: FTS5, the rowid, the write-ahead log, the busy timeout, the size of the
file. A mock of SQLite proves that the code calls the methods it thinks it calls — not that the query finds the hit, that
the write is atomic, or that the file stays small.

So the integration layer opens a **real database file** for every test and lets the library do what it does. The price is
a file per test, and it is paid in seconds.

### A file, not `:memory:`

`TestDatabase` creates a file in the temp directory and deletes it afterwards. `:memory:` would be faster, and it would
be wrong: a `:memory:` database belongs to the connection that opened it, and the library opens connections per operation
and keeps a pool. Every connection of the pool has to see the same database — that is the behaviour under test, and a
file is what an application has. The read-only mode needs a file as well.

### The names are sentences

The name of a test is a sentence that names the rule, not the method (ADR-0049):
`A_soak_of_readers_and_one_writer_keeps_the_count`,
`An_input_at_a_word_start_beats_a_more_popular_entry_that_has_it_in_the_middle`.

A failed test is read in the report of the CI, and a sentence says what the rule is. The report of a red build becomes a
list of the rules that broke, not a list of methods that failed. The classes speak the same language: `<Subject>Tests`
for the behaviour, `<Subject>Performance` for the measurements — a filter, the test explorer and the mutation check
understand one vocabulary.

### The oracle

A test needs something to compare its result with. There are three kinds of oracle in this repository.

1. **A fixed expectation.** The golden queries write their expectation out: five entries, and the order the ranking puts
   them in. The order is in the test, so a change of it is visible in the diff (ADR-0019).
2. **The library that is replaced.** LUI has its own measures of similarity. Each of them is compared with the library it
   replaces (`F23.StringSimilarity`) — for known pairs, for ten thousand random pairs and for the lengths 0 … 12,
   **without a tolerance** (ADR-0030, issue #30). The old library is the oracle of the new one, and it is dropped when
   the equivalence is proved.
3. **The file itself.** The schema of an index is compared byte for byte with the golden schema; the report of a
   measurement is compared with the report in the README (ADR-0060).

A test without an oracle compares the result with itself. It proves nothing.

### A measurement is not a test

A test asserts a **rule**: it is true or false, on every machine, forever. A measurement asserts a **number about a
machine**: it is true or false on the machine it was measured on, today. Mixing the two makes the suite flaky and the
promise empty — a suite that is red because the runner was busy teaches its readers to ignore red.

So the measurements live in a layer of their own, they run only when somebody asks for them, and their numbers are
written into the budgets — one file, with the machine beside them in the record (ADR-0056, ADR-0041). A size (the file,
the memory) belongs to everybody; a time belongs to the machine it was measured on.

### The guards read the documentation

Some rules of this repository are not in the code at all: the API page lists the public types, the map lists the test
files, the README lists the measured numbers, the specification is deleted and has to stay deleted. A rule that only a
document carries needs a **guard that reads the document** — otherwise the document and the code drift apart, and the
drift is found by a reader, or never.

| The guard | It reads | It fails when |
|---|---|---|
| `PublicApiDocumentationTests` | `docs/api.md` and the two assemblies | a public type is not on the page, or the page names a type the code does not have |
| `TestLandscapeTests` | `docs/testing.md` and the tree | a test file is not listed, a row names a file that does not exist, a row's layer does not match its project, a unit test opens a database |
| `LinkCheckTests` | every markdown file of the repository | a relative link points at nothing |
| `ReadmeBenchmarksTests` | the README and the committed reports | the report in the README is not the report of the run |
| `SpecificationIsGoneTests` | the tree around the papers | the deleted specification is named somewhere |

The unit of the map is the **file**, not the class: a file holds one test class, and a file is what a reader opens.

### The mutation check: a green test proves nothing until it has been seen red

A test that never fails proves nothing — it may assert the wrong thing, or nothing at all.
[`tools/mutation-check.sh`](../tools/mutation-check.sh) takes the repository into a temporary copy, **breaks a rule on
purpose** and asks the guard to react. Every mutation says which of the two mechanisms is supposed to catch it:

- `expect_caught` — the test of the filter runs and fails. The rule is a real test. A build that refuses the mutation is
  a **failure** here: the test was never asked, and a mutation caught for the wrong reason is worth nothing.
- `expect_refused` — the build refuses it with a named error. The rule is enforced by the toolchain on purpose — the
  central package management, for example.

Every mutation runs on its own copy, and the CI runs the mutations in four shards side by side.

### The gates: a skip that says why

A test that cannot run — the whole word list is not there — is **skipped with a reason**, not green.
`[FullWordListFact]` asks the gate when the test is about to run, and the report says why it was skipped.

A test whose verdict depends on the clock carries `[Trait("Category", "Timing")]`: on a loaded runner it can turn red
without a fault — the most expensive kind of flake, because it costs the trust in the whole suite. The trait makes the
fast local loop expressible (`dotnet test Lui.sln --filter "Category!=Timing"`), and the guard `TimingTraitTests` finds
the tests that read the clock — in the body or in a helper of their file — and fails when one of them has no trait.

## How to write a new test

1. **The layer.** A rule without a database belongs to the unit layer, a rule about SQLite to the integration layer, a
   rule about the shape of the repository or of the public API to the architecture layer. A unit test that opens a
   database fails the build.
2. **The file.** The subject of the rule is the folder, and the folder is the namespace. A new behaviour of a subject goes
   into the file of that subject — not into a new file named after the review that found it.
3. **The name.** A sentence that names the rule. The class carries the suffix of its layer.
4. **The row in the map.** The [test map](./testing.md) gets a row: the file, the layer, what it proves, and the rule it
   holds up. The map is a guard — a file that is not listed fails the build.
5. **Seen red.** Run the test against the code before the change: it has to fail for the reason it is written for. A test
   that was never seen failing proves nothing — that is what the mutation check is for.
6. **The mutation.** If the rule is one the guards hold up, the mutation that breaks it comes with it: the mutation check
   has to catch it, and it has to say which mechanism catches it.
7. **The timing.** If the verdict depends on the clock, the trait `Category=Timing` comes with it — the guard finds the
   test otherwise.
8. **The comment.** Where the test came from stays above it: `// review of pull request 6, B1: a value-type payload`.

## What is deliberately not done

- **No mocks of SQLite.** The behaviour under test is the behaviour of the database. A mock proves the call, not the
  result.
- **No coverage gate.** Coverage counts the lines that were executed; it says nothing about the rules that are held up. A
  rule without a test is invisible to a coverage number, and a line without a rule counts as covered.
- **No time budgets in the CI.** A time belongs to the machine it was measured on (ADR-0041), and the runner of the CI is
  not that machine. The CI runs the tests, the measurements run on purpose, and the numbers are written into the budgets
  with the machine beside them.
- **No test of the internals.** The rules of the repository are read from the outside — the assemblies, the tree, the
  files of the documentation. The internals are not a contract; a test that needs a white box asks for an API that should
  not exist.

## The lanes of the CI and the release gate

The gates of every pull request are in [`ci.yml`](../.github/workflows/ci.yml), and the measurements have a lane of their
own ([`performance.yml`](../.github/workflows/performance.yml)). Every job names its image, has a timeout, and stops a
test that hangs with a full dump behind it.

| The lane | What it runs | What it is there for |
|---|---|---|
| Format | `dotnet format --verify-no-changes` | a format error is reported in a minute, not after every test |
| Workflows · actionlint | every workflow of the repository | a mistake in the workflow of the release is found before the release, not in it |
| Build · Test | the three layers, the tests of the sample, the client of the sample | the whole tree on every pull request |
| The normalisation parity over the whole word list | the one test that needs the 685,829 words — the fetch before it | the rule of the normalisation exists twice, as SQL and as C#, and the two have to agree |
| The guards bite | the mutation check, in four shards | a green test proves nothing until it has been seen red |
| Package · Smoke test | the packed library in a throwaway application | the code of the README is part of the test |
| Windows | the three layers and the sample on `windows-2025` | the paths, the pragmas and the case of the file names are promises that only Windows can break |

The **release gate** is the same set of lanes: the release calls this workflow instead of keeping a gate of its own
(ADR-0048). A release passes exactly the gates of a pull request, and the key that pushes the packages is stored for an
hour, not longer.
