# 0019 — The test tree: three layers, one shared infrastructure, one map that is a guard

- **Status:** Accepted
- **Date:** 2026-11-03
- **Affects:** `tests/` (the whole tree), `Directory.Packages.props`, `Lui.sln`,
  `src/Nordmann.LUI/Nordmann.LUI.csproj` and `src/Nordmann.LUI.Hosting/Nordmann.LUI.Hosting.csproj`
  (`InternalsVisibleTo`), `docs/testing.md`, `docs/architecture/alignment.md`, `AGENTS.md`,
  `tools/mutation-check.sh`, `.github/workflows/ci.yml`
- **Builds on:** [0004](./0004-dependencies.md) (the dependency set — a test project is not a runtime dependency),
  [0013](./0013-the-api-shape-before-the-first-release.md) (the API the tests hold up),
  [0014](./0014-seams-for-the-second-content-source.md) (the architecture rules the tests read)

## Context

The repository had **one** test project: `tests/Nordmann.LUI.Tests`, with 466 tests in 35 files. But
`AGENTS.md` and `docs/architecture/alignment.md` both said there were **three** test projects — unit,
architecture, integration. The documentation described a pyramid the tree did not have.

The flat tree had the consequences one expects:

- **No fast lane.** Every test opened a database file. The rules that need no database — the normalisation, the word
  start, the similarity, the FTS5 expression — ran inside the same 36-second process as the 3-million-entry
  measurements. The measured cost of the pure rules was **9 seconds**; the rest was the setup.
- **No way to select a layer.** Not one `[Trait]` in the whole suite, no `xunit.runner.json`. "Run the fast tests"
  was not expressible, so nobody ran them alone.
- **The infrastructure was copied, not shared.** `tests/Shared/Repository.cs` was `<Compile Include>`-linked into
  both test projects — the same file compiled twice. The `Recipes()`/`Fixture` helpers were pasted verbatim into
  two files. The word list path was spelled eight times in four spellings. The `CREATE TABLE tag` DDL was copied
  four times.
- **A test that proved nothing was green.** `NormalizationParity.The_normalizer_and_the_tokenizer_agree_on_the_full_word_list`
  reported *Passed* in 1 ms when the word list was not there — it compared nothing with nothing. Only
  Only `publish.yml` demanded the whole word list; CI never fetched it.
- **A measurement could pass without measuring.** `QueryPerformance` asserted its budgets only at
  `entries >= 3_000_000`; below that it asserted nothing.
- **A dead dependency looked like a dependency.** `Alba 8.5.3` was pinned in `Directory.Packages.props` and
  referenced by no project. The architecture rule checked reference → version, never version → reference.

## Decision

**Three test projects, one per layer, and a shared infrastructure project.**

| Project | Holds | Needs a database |
|---|---|---|
| `tests/Nordmann.LUI.Unit.Tests` | the rules on their own | no |
| `tests/Nordmann.LUI.Integration.Tests` | the library against a real SQLite file | yes |
| `tests/Nordmann.LUI.Architecture.Tests` | the shape of the repository and the public API | no |
| `tests/Nordmann.LUI.Testing` | the shared setup — the database, the corpora, the fixtures | — |

`Nordmann.LUI.Testing` is a **library, not a test project** (`IsTestProject=false`, `IsPackable=false`). It holds
`TestDatabase`, `TestCorpus`, `ApplicationTable`, `RecipeStock`, `Chip`, `TypoMaker` and `Repository` — the things
that were copied, pasted or linked before. It depends on the **public** API of the library only, so it needs no
`InternalsVisibleTo`; the test projects keep theirs.

**The unit layer needs no SQLite.** It does not reference `Microsoft.Data.Sqlite` at all. The unit lane is ~1
second; the integration lane ~33; the architecture lane ~0.6.

**The tree has two packages since ADR-0018** — the core and the hosting. The test tree does not care: the
integration layer references both, the unit layer only the core, and the architecture layer reads both from
disk. The `InternalsVisibleTo` of both packages name the new projects.

**The cut is by file, not by test method.** A file goes to the layer it belongs to. `RowIdTests` needs a database
for one of its tests, so it stays in the integration layer — a file was not split to make a number look better.

**A missing input is a skip with a reason, never a green.** `FullWordListFactAttribute` reads the gate at
discovery: the test is skipped and says why when the list is missing. The guard that the list is really there is
the fetch — `tools/fetch-wordlist.sh` checks the 685,829 lines and fails loudly (ADR-0058). The measurements need
no gate of their own: they live in their own layer.

**A measurement is never a test of behaviour.** The measurements live in a layer of their own since ADR-0058; the
budgets are asserted at every size, not only at three million.

**The map of the tests is a guard, not a document.** `docs/testing.md` says which test holds which rule of the
specification up. `Test_Landscape` reads the map and the tree and fails when they disagree: a test **file** that is
not listed, a row that points at a file that does not exist, a row whose layer does not match the project the
file lives in, a file listed twice, a layer without tests. The unit of the map is the file, not the class: a file is
what a reader opens, and a guard that parses class names out of the source finds the prose of its comments. A map
that rots is worse than no map.

**The unit layer opens no database — and a guard says so.** It references neither `Microsoft.Data.Sqlite` nor the
shared infrastructure (`Nordmann.LUI.Testing`, which opens databases), and no line of its code names `LuiCatalog`,
`TestDatabase` or a `Sqlite…` type. `Microsoft.Data.Sqlite` reaches the unit project transitively through the core,
so the reference alone would not keep a unit test from calling `LuiCatalog.Open`; `Test_Landscape` reads the project
file and the source.

**The pin and the reference are checked in both directions.** `Repository_Layout` now also asserts that every
`PackageVersion` is referenced by some project. `Alba` is removed.

## What was measured

The baseline, before the change (on the local disk, `dotnet test Lui.sln`, 466 + 29 tests, 0 failures), and the
result after it. The times are what the test runner reports — the test run itself, without the build:

| | before | after |
|---|---:|---:|
| unit layer (the rules, no database) | — (mixed into the 36 s below) | **139 tests in 1.0 s** |
| integration layer (SQLite) | — | 352 tests in 33 s (10 skipped) |
| architecture layer | 29 tests in 1.0 s | 39 tests in 0.6 s |
| the whole suite | 495 tests in 37 s | **530 tests in 35 s** |
| test files | 35 in one folder | 8 unit · 31 integration · 6 architecture · 7 shared · 0 linked |
| `[Trait]` attributes | 0 | 0 — the layer *is* the project |

The suite grew, because the gaps were filled: the JSON payload serialiser (a public type without a test), the
lazy similarity of ADR-0008 (a promise about work, provable only by counting the calls of the measure), the
ranking orders, the quality floors of §19.

**One gap was found by the mutation check itself.** It changed the lower-case "ä" of the German
transliteration to "a" instead of "ae", and the suite stayed green. The table has seven entries — "ä" "Ä"
"ö" "Ö" "ü" "Ü" "ß" — and the suite used the upper-case "Ä" ("Äpfeln"), the lower-case "ö" ("Größe") and the
lower-case "ü" ("müller"), but never the lower-case "ä" and never the upper-case "Ö". Two of the seven
entries of the table could have been wrong, and nothing would have said so. Both cases are in now, with the
other letters in both cases as well.

**A mutation that changes nothing is a green that proves nothing.** The mutation of the transliteration was
itself wrong: its pattern, `replacements['ä']`, is a bracket expression in a regular expression and matched
no file — the script read the untouched build as a refusal by the compiler. The script now fails when a
filter matches no test, and the pattern escapes its brackets.

## Consequences

- **The suite is three commands instead of one.** `dotnet test Lui.sln` still runs everything; but the fast lane
  is now `dotnet test tests/Nordmann.LUI.Unit.Tests`, and it is the lane a change to a rule is proved in.
- **The infrastructure is a dependency of the tests, not a link.** `tests/Shared/` is gone. The `Repository` helper
  — which reads the *tree* rather than a hand-written list, the way the architecture rules do — is compiled once.
- **A test file that is not in the map fails the build.** That is the price of the map: a new test file has to
  say which rule it holds up. It is also the benefit — the question "which test proves §10.4?" has an answer that
  cannot go stale.
- **`Nordmann.LUI.Testing` is public API of the test tree.** Everything in it is `public`, and it is not
  packable — it never reaches a consumer.
- **The corpus paths are in one place.** `TestCorpus` knows where the sample list, the edge cases, the full list
  and the golden schema are. Eight tests each built their own path out of the repository root; a path spelled
  eight times is wrong once.
- **The three layers do not have the same cost.** A change to a rule is proved in 10 seconds; a change to the
  schema costs 40. The measurement is what it always was — a person, on purpose.

**Verified by:** `tests/Nordmann.LUI.Unit.Tests/` (the rules without a database, the JSON payload serialiser, the
ranking and the lazy similarity), `tests/Nordmann.LUI.Integration.Tests/` (the library against SQLite, the
the measurements in their own layer), `tests/Nordmann.LUI.Architecture.Tests/Test_Landscape.cs` (the map and
the tree agree — a file is a test file by any form of its test attribute, `[Fact(Skip = …)]` included; two test
files of the same name in two projects are told apart; the unit layer opens no database), `tests/Nordmann.LUI.Architecture.Tests/Repository_Layout.cs` (every pinned version is used);
`dotnet format --verify-no-changes`; `tools/mutation-check.sh`.

## Note (2026-11-08, issue #76)

The tree this decision describes changed with the restructure of the tests (epic #72). The decision stands; the
history above stays as written, and these are the differences:

- **The library still has three layers; the tree has a fourth test project.** The tests of the sample moved out of
  the integration layer into `samples/Lui.Sample.Tests`, beside the sample they test. The library's integration
  project no longer references the sample or ASP.NET Core. The map (`docs/testing.md`) lists them under their own
  section, *Sample*, and `Test_Landscape` knows the project by its directory and calls its layer `Sample`.
- **`TestCorpus` is `TestFiles`,** and the test data moved from `tests/Nordmann.LUI.TestData/` to `tests/TestData/`.
  Where the text above says `TestCorpus`, read `TestFiles`.
- **The four experiments are gone** (ADR-0010, 0011, 0015, 0016 say where their code is in the history). The
  numbers of the table above were measured with them in the tree.

## Note (2026-11-09, issues #77, #89)

[ADR-0049](./0049-the-tests-are-sorted-by-subject-and-carry-one-suffix.md) sorts the files of the layers into folders by
subject (a folder is a namespace) and gives every test class one suffix. Its table maps the old names in this ADR to the
new ones (`Test_Landscape` is `TestLandscapeTests`, `Repository_Layout` is `RepositoryLayoutTests`). The map guard now also
compares the **layer** of a row with its project, and reads the test projects from the tree rather than from a list.

## Note (2026-11-12, issue #122)

[ADR-0058](./0058-the-measurement-is-a-layer-of-its-own.md) gives the measurement a layer of its own
(`tests/Nordmann.LUI.Performance.Tests`) and takes the variables of the environment away. The decision stands; the history
above stays as written, and these are the differences:

- **The tree has a fourth layer.** The six files that read the clock moved from the integration layer into
  `tests/Nordmann.LUI.Performance.Tests`. The gate of the measurements is the layer — there is no variable that switches
  them on, and the scenario of a run lives in `performance.scenario.json`, in the repository.
- **The unit layer names the shared infrastructure.** The rules of the budget live there (`PerformanceBudget`,
  `BudgetMachine`), so `BudgetMachineTests` is a unit test and the unit project references the infrastructure. The rule of
  the unit layer stands: it opens no database — no line of its code names `LuiCatalog`, `TestDatabase` or a `Sqlite…` type,
  and the guard reads the project file and the source.
- **The guard of the whole word list is the fetch.** `tools/fetch-wordlist.sh` checks the 685,829 lines and fails loudly;
  the lane of the parity fetches the list before it runs the test.
