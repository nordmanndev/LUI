# Milestone v0.4.0 — run plan

Parameters: merge by agent: **yes**, once a review by an independent sub-agent finds nothing left and CI is green on the
head (this milestone only; merges into `0.4.0` only, never `0.4.0 → dev`, `dev → main`, a tag or a publish) |
PR merged first: **none** (the open PRs #220, #222, #256 against `dev` are not part of this run) | extra limits: **none**
beyond the round rule of `AGENTS.md` (only the issues of milestone v0.4.0 come into `0.4.0`) | checks / verification
commands: **every check of `ci.yml` green** — see "Verification" below.
Reviewer model: opus (fresh sub-agent per round) | implementer: sonnet

Planned on 2026-11-20 with the owner. Epic: #258 (type Feature). Base branch of the round: **`0.4.0`**, branched from `dev`
at `22edaff`. Every pull request branches from `0.4.0` and targets `0.4.0`, one per issue, in the order below; the next one
starts when the one before it is merged. The implementation starts when the owner merges the pull request that brings this
file into `0.4.0`.

| # | Issue | Why here | Depends on | Status | PR |
|---|---|---|---|---|---|
| 1 | #259 API freeze moves to 1.0.0 (ADR-0071) | every later break cites it; docs only | — | merged | #279 — the freeze comes with 1.0.0; no `[Obsolete]` bridges before it |
| 2 | #274 Baseline: v0.3.0 against itself (`QueryBenchmarks`) | #258 needs the noise before the first code PR | #259 | merged | #281 — the baseline stands as measured; performance only at the end (#275), observations in #280 |
| 3 | #261 Definitions and typed handles, `long` keys (ADR-0073) | the contract everything builds on — riskiest, first | #274 | stopped — round limit, owner decision | #282 |
| 4 | #269 Payload variants, typed linked builder (ADR-0073) | extends the types of #261 | #261 | open | — |
| 5 | #270 Key types `int`/`string`/`Guid`, LUI format 2 (ADR-0073) | the file format #260/#272/#271 add to | #269 | open | — |
| 6 | #265 Runtime in the catalog, 0 B open, several processes (ADR-0075) | the query path; its budget risk shows early | #270 | open | — |
| 7 | #260 Payload registry, type and format fingerprint (ADR-0072) | needs definitions and the handle cache | #265 | open | — |
| 8 | #262 `hit.Payload` loaded with the query, `CheckPayloads()` (ADR-0074) | needs the registry; its own budget | #260 | open | — |
| 9 | #272 State of a linked index: `NeedsInitialSync`, idempotent `CreateTriggers()` (ADR-0077) | precondition of #271 and #266 | #262 | open | — |
| 10 | #271 Linked `Sync()` in batches, generation, merge, lease (ADR-0077) | needs `NeedsInitialSync`; `Initialize()` needs the lease | #272 | open | — |
| 11 | #273 Maintenance by name (ADR-0076) | needs `LuiKeyType` and the fingerprint; #266 adds to `LuiIndexInfo` | #271 | open | — |
| 12 | #266 `catalog.Initialize()`, `AddLuiOutbox`, host pattern (ADR-0076) | puts every step before it together | #273 | open | — |
| 13 | #268 Remove every replaced member, move every caller (ADR-0073) | only when every replacement exists | #266 | open | — |
| 14 | #263 Sample and documentation | describes the finished API | #268 | open | — |
| 15 | #221 Align with the Nordmann standards | changes wiring, labels and the rule book — after the code | #263 | open | — |
| 16 | #275 Performance analysis: the budget against the baseline | the budget is read at the end of the release | #221 | open | — |
| 17 | #276 Release 0.4.0 into the branch; open `0.4.0 → dev` | the last step; the release itself is the owner's | #275 | open | — |

**Decision (owner, 2026-11-21, #274):** the spread of three of 16 cases is above 15 % (`titles/b/Linked` 17.7 %,
`titles/apfelkuc/Embedded` 16.1 %, `words/haus/Linked` 29.0 %); the numbers are taken as they are. The baseline is not a
gate: performance is checked once, at the end (#275) — did 0.4.0 get markedly worse or better than 0.3.0. Observations about
performance on the way go into one performance issue for the next milestone, not into the PRs. The code PRs of this run
need no per-PR A/B measurement (owner, 2026-11-21: performance is looked at at the end).

**Stopped (2026-11-21, #261 / PR #282):** three review rounds are used and round 3 still had one blocker (a configuration
failure inside `Create(a, b, c)` did not name its definition). The blocker is fixed on the branch (commits `0d3c4da`,
`0e8cbff`; format, build and all four suites green locally), but no reviewer has seen that state, and the rules forbid a
fourth round. Options: **A** the owner merges #282 as is, or reads the last two commits (about 100 lines) and says
"merge"; **B** a fourth, narrow review of only those two commits (an exception to the round rule); **C** rework. The
rounds found, in turn: round 1 three blockers (the `LuiEntry` constructor, the failing definition not named, the layering
guard), round 2 one blocker (a transaction write on a dropped index), round 3 one blocker (the name in one more branch) —
each narrower than the last. Recommendation: **B**. #269 to #268 build on #261, so nothing else can proceed.

Risks of the whole run:

- **The SDK.** `global.json` pins SDK `10.0.403` (`rollForward: latestPatch`); the image ships only `10.0.114`. The
  environment now allows `builds.dotnet.microsoft.com` and its setup script installs `10.0.403` into `/usr/lib/dotnet`
  (owner, 2026-11-20). **At the start of every session, check `dotnet --list-sdks`.** If `10.0.403` is missing, run
  `curl -sSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh && bash /tmp/dotnet-install.sh --version 10.0.403 --install-dir /usr/lib/dotnet`;
  if that fails too, stop and tell the owner. Never change `global.json` to get around it.
- **Breaking release, additive PRs.** From #261 to #266 the new API lives **next to** the old one (the old calls are a thin
  layer over the new ones); only #268 removes. A PR that removes or renames an old member before #268 breaks the plan.
- **The guards of the repository fail loudly on every public change** — each code PR has to move them in the same PR:
  `PublicApiDocumentationTests` (every public type and member on `docs/api.md`), `TestLandscapeTests` (every test file in
  `docs/testing.md`), `AdrIndexTests` (every record in `.agents/adr/README.md`), the guards of `PublicApiShapeTests` named in
  each work plan, and `tools/mutation-check.sh` (it runs in CI in four shards; a mutation whose `sed` no longer matches the
  file it edits **fails the check** — after changing a source file, `grep -n '<path>' tools/mutation-check.sh` and keep its
  mutations matching).
- **The performance budget** (#258) is measured in a cloud container that is a new machine in every session: **the A/B
  rule of #274** — `v0.3.0` and the head measured in the same session, alternating, compared only with each other.
- **The API stays synchronous** (the owner, 2026-11-20): there is **no `SyncAsync`**. Where an issue says `SyncAsync`, read
  the synchronous `Sync(IAsyncEnumerable<LuiEntry<…>>, LuiSyncOptions?, CancellationToken)` (addenda of #258, #261, #269,
  #263, #268). The guard `The_public_api_is_synchronous` stays without an exception.
- **Long measurements.** #271 (1,000,000 rows) and #275 take an hour or more; run them in the background and keep the
  output (`tools/load-run.sh`-style: the whole log, not a summary).

## Conventions of the run (every PR)

- **Branch:** `feat/<issue>-<slug>` (docs-only: `docs/<issue>-<slug>`, measurements: `perf/<issue>-<slug>`), from the
  up-to-date `0.4.0`.
- **Commits:** `<type>(<scope>): <subject> (#<issue>, ADR-<number>)`, the body says **why**. Author
  `Claude <noreply@anthropic.com>` (the owner, 2026-11-20; #259 corrects the line in `AGENTS.md`). **Red first:** the
  commit that adds a test and sees it fail comes before the commit that makes it green — the PR history shows it
  (`test(…): … — red (#N)`, then `feat(…): … (#N, ADR-…)`, as in `115674b` / `536f046`).
- **Every PR:** a `## Verification` section with the real output of the commands below; the list of test files touched;
  the `CHANGELOG.md` `## [Unreleased]` entry with a migration note when the public API moved; the rows of the new public
  members on `docs/api.md`; the new test files in `docs/testing.md`; the ADR and its row in `.agents/adr/README.md` in the
  PR of the first issue of that ADR (0072 #260, 0073 #261, 0074 #262, 0075 #265, 0076 #273, 0077 #272), later issues add
  sections to the same record.
- **Labels:** set `status:in-review` on the issue when its PR is open; the PR closes the issue (`Closes #N`).
- **Review and merge:** after CI is green, start a **fresh Opus sub-agent** that reviews the PR against the issue (body and
  addendum), this work plan and `AGENTS.md`. Fix what it finds, push, and start a **new** sub-agent for the next round,
  until a round finds nothing. Then merge with a **merge commit** (as the repository does). Never delete `0.4.0` or `dev`;
  after a merge, check that `0.4.0` still exists. Update the `Status` and `PR` columns of the table above in the next PR's
  first commit (`docs(plan): …`).
- **Numbers of the records:** 0071–0077 as #259 fixes them. The sample's client series (#212, PR #256) claims 0071 too —
  that is its series to renumber; this run does not touch it.

### Verification (the commands of `ci.yml`)

```bash
dotnet --list-sdks                       # must list 10.0.403 (global.json) — stop otherwise
dotnet restore Lui.sln
dotnet format Lui.sln --verify-no-changes --no-restore
dotnet build Lui.sln --configuration Release --no-restore
dotnet test tests/Nordmann.LUI.Unit.Tests          --configuration Release --no-build
dotnet test tests/Nordmann.LUI.Architecture.Tests  --configuration Release --no-build
dotnet test tests/Nordmann.LUI.Integration.Tests   --configuration Release --no-build
dotnet test samples/Lui.Sample.Tests               --configuration Release --no-build
(cd samples/Lui.Sample && npm ci && npm test)
bash tools/package-smoke-test.sh                   # runs dotnet pack and every C# block of the README
python3 tools/tutorial-check.py
bash tools/mutation-check.sh --shard 1/4           # … 2/4, 3/4, 4/4 — at least the shards whose guards the PR touched
```

`actionlint` runs in CI for a PR that changes `.github/workflows/`. The performance layer
(`tests/Nordmann.LUI.Performance.Tests`) runs only where a work plan names it.

### The A/B measurement (from #274 on)

```bash
git worktree add ../lui-v0.3.0 v0.3.0                 # the baseline, once per session
export TMPDIR=/tmp                                   # a local disk (tools/benchmarks.sh refuses a network one)
# alternate: baseline, head, baseline, head — the same session, nothing else running
dotnet run -c Release --project ../lui-v0.3.0/benchmarks/Nordmann.LUI.Benchmarks -- --filter '*QueryBenchmarks*' --exporters github --artifacts /tmp/ab/base-1
dotnet run -c Release --project benchmarks/Nordmann.LUI.Benchmarks            -- --filter '*QueryBenchmarks*' --exporters github --artifacts /tmp/ab/head-1
```

Compare the medians per case of the same session against max(+3 %, 2 × the noise of #274). Do **not** commit these reports
to `benchmarks/results/` (`ReadmeBenchmarksTests` wants the five reports of one run); quote the numbers in the PR and, where
the issue says so, in `docs/benchmarks.md`.

## Work plan per issue

### 1. #259 API freeze moves to 1.0.0 (ADR-0071)

- **Approach:** a record and its consequences, no code. Write ADR-0071 "the freeze comes with 1.0.0": the reason is
  semantic versioning (`0.x` promises no stable API), not the number of users; every break needs a reason that makes the
  API clearer, a minor version, a migration note and an ADR; no `[Obsolete]` bridges before 1.0.0. ADR-0069 gets the status
  "§4 superseded by ADR-0071" and keeps the rest. `AGENTS.md`: restate the "Never break the public API …" line with
  ADR-0071, and correct the commit identity of the agent to `Claude <noreply@anthropic.com>` (addendum). `CHANGELOG.md`
  `## [Unreleased]`: one sentence that the API may change until 1.0.0.
- **Where:** `.agents/adr/0071-the-freeze-comes-with-1-0-0.md` (new; copy the header of
  `.agents/adr/0069-the-freeze-of-0-1-0-is-lifted-for-0-3-0.md`: Status, Date, Supersedes, Builds on, Affects, Issue),
  `.agents/adr/0069-…md` (status line), `.agents/adr/README.md` (row 0071, topic `API`, like row 0069), `AGENTS.md`,
  `CHANGELOG.md`.
- **Tests:** none new (docs only); the guards that hold it: `AdrIndexTests` (the row), `AdrPathTests` (the paths of the
  "Affects" list exist), `LinkCheckTests`.
- **Verification:** `dotnet test tests/Nordmann.LUI.Architecture.Tests` plus the format check; the full list above before
  the PR.
- **Risks / traps:** the "Affects" list of an ADR is checked by `AdrPathTests` — list only files that exist. Do not touch
  `Directory.Build.props` (the version moves in #276).
- **Stop if:** the wording of the rule in `AGENTS.md` would also change the round rule of 0.4.0 — that rule stays as the
  plan PR wrote it.

### 2. #274 Baseline of 0.4.0: v0.3.0 against itself

- **Approach:** measure, no code. In one session: `git worktree add ../lui-v0.3.0 v0.3.0`, `TMPDIR` on a local disk, run
  `QueryBenchmarks` of the worktree **5 times** with the default job (the command of "The A/B measurement", artifacts
  `/tmp/baseline/run-1` … `run-5`). Per case (corpus × input × source = 16 cases): the 5 medians, the median of medians, the
  spread (max − min of the 5 medians, in % of the median of medians), and the budget max(+3 %, 2 × spread). Write the
  section "The baseline of 0.4.0" in `docs/benchmarks.md` with the machine (CPU, cores, RAM, OS, .NET, SQLite, the disk from
  the report's `[Disk]`), the date, the command, the table, and the A/B rule in words a later PR can follow.
- **Where:** `docs/benchmarks.md` only (after "The first load (issue #32)", before "The tables of the run"). Model: the
  section "The write lock of a linked sync (issue #31)".
- **Tests:** none new; `ReadmeBenchmarksTests` and `LinkCheckTests` stay green — the reports in `benchmarks/results/` are
  not touched.
- **Verification:** `dotnet test tests/Nordmann.LUI.Architecture.Tests`; the raw medians of the 5 runs in the PR body.
- **Risks / traps:** one run of `QueryBenchmarks` takes roughly 10–20 minutes; run in the background, keep every report.
  Nothing else may run meanwhile (no build, no test). A run on a network disk is worthless (`[Disk]` must be local).
- **Stop if:** the spread of a case is larger than 15 % — the budget would be meaningless; report the numbers to the owner.

### 3. #261 Index definitions and typed handles, `long` keys (ADR-0073)

- **Approach:**
  1. **Builders without catalog.** `LuiIndexKindBuilder`, `LuiEmbeddedIndexBuilder`, `LuiLinkedIndexBuilder` lose their
     `LuiCatalog _catalog` and `string _name`; they collect an internal description (new `internal sealed record
     IndexDescription(LuiIndexKind Kind, IndexOptions Options, LinkDescription? Link, LuiKeyKind? KeyKind)` in
     `Nordmann.LUI` root, or next to `IndexOptions` in `Schema` — keep it in the layer the builders already use). Their
     public `Create()` throws `InvalidOperationException("pass a definition to catalog.Create")`. `WithKey(LuiKeyKind)`
     records the kind for the old path; inside a definition it throws `LuiConfigurationException`.
  2. **Definitions.** `LuiIndexDefinitionBase` (abstract, `private protected` constructor, `Name`, `Source`
     (`LuiIndexSource` — **new public enum `Embedded`/`Linked` in this PR**, #273 only reuses it), `KeyType`, `PayloadType`
     (null here), `ToString()` → `"tags (embedded, long)"`), `LuiIndexDefinition<TKey>` and `LuiLinkedIndexDefinition<TKey>`
     (sealed, `where TKey : notnull`). `Name` is stored at once; the validation and the builder function run once, lazily,
     under `LazyInitializer`/`Lazy<T>(LazyThreadSafetyMode.ExecutionAndPublication)` and keep the result or the exception;
     a failure is `LuiConfigurationException` (new, derives `LuiException`).
  3. **The catalog creates from a description.** One internal core `LuiCatalog.CreateCore(string name, IndexDescription,
     string keyType)` holds what `LuiEmbeddedIndexBuilder.Create()` / `LuiLinkedIndexBuilder.Create()` do today (read them
     first; they call `IndexStore.Create`). `Create(LuiIndexDefinition<TKey>)`, `Create(LuiLinkedIndexDefinition<TKey>)`,
     `Create(params LuiIndexDefinitionBase[])` (one transaction, duplicate names → `ArgumentException` before any write)
     and the old `Create(name, fn)` / `CreateLinked(name, fn)` all go through it; the old path builds a
     `LuiIndexDefinition<long>` (or, for `WithKey(LuiKeyKind.Text)`, an internal `LuiIndexDefinition<string>` — the allowed
     key set is one internal method, `KeyTypes.Validate(Type, LuiIndexSource)`, that #270 opens to `int`/`string`/`Guid`).
  4. **Handles.** `LuiIndex<TKey>` (abstract, internal ctor), `LuiEmbeddedIndex<TKey>`, `LuiLinkedIndex<TKey>` (sealed),
     `LuiEntry<TKey>` (sealed record), `LuiQuery<TKey>`, `LuiResult<TKey>` (sealed classes), `LuiHit<TKey>` (sealed record,
     **no public constructor with parameters** — init properties, internal factory). In this PR they wrap today's internal
     `EmbeddedIndex` / `LinkedIndex` (convert `TKey` ↔ `LuiKey` at the edge); #265 moves the runtime into the catalog. The
     members are exactly the tables of the issue, with the addendum: **no `SyncAsync`** — `LuiEmbeddedIndex<TKey>` has
     `Sync(IAsyncEnumerable<LuiEntry<TKey>>, LuiSyncOptions?, CancellationToken)` that blocks, like today's
     `ILuiEmbeddedIndex.Sync(IAsyncEnumerable<LuiEntry>)` (`src/Nordmann.LUI/Indexes/EmbeddedIndex.cs:238`).
  5. **`Index(definition)`** opens both sources; it compares the stored with the declared definition (the compared fields
     of the issue) and throws `LuiDefinitionMismatchException` naming the fields, or `LuiIndexNotFoundException`. A dropped
     index → `LuiIndexNotFoundException` on the next operation of an old handle; a disposed catalog →
     `ObjectDisposedException`.
  6. ADR-0073 (new): the definition as the single declaration, builders describe / catalog creates, compared vs.
     runtime-only fields, the recipe for a changed definition, **the API stays synchronous also for an asynchronous source**,
     `LuiConfigurationException`, concrete handle classes.
- **Where:** `src/Nordmann.LUI/` (root, the public types): new files `LuiIndexDefinitionBase.cs`, `LuiIndexDefinition.cs`,
  `LuiLinkedIndexDefinition.cs`, `LuiIndexSource.cs`, `LuiConfigurationException.cs`, `LuiIndexOfKey.cs`,
  `LuiEmbeddedIndexOfKey.cs`, `LuiLinkedIndexOfKey.cs`, `LuiEntryOfKey.cs`, `LuiQueryOfKey.cs`, `LuiResultOfKey.cs`,
  `LuiHitOfKey.cs` (the generic forms of a name that already has a non-generic file get the suffix `OfKey`; #269 uses
  `OfKeyAndPayload`); changed: `LuiCatalog.cs` (lines 382–437, the old `Create`/`CreateLinked`/`Index` become thin),
  `LuiIndexKindBuilder.cs`, `LuiEmbeddedIndexBuilder.cs`, `LuiLinkedIndexBuilder.cs`. Models: `LuiTypedIndex.cs` (a typed
  view over the internal index), `LuiDefinitionMismatchException.cs` (an exception with named fields),
  `Schema/IndexDefinition.cs` (the stored definition). The layering of `InternalLayeringTests`: the new handles and
  definitions are part of the facade (root) — no type below the root may reference them.
- **Tests (red first, one commit per group):**
  - `tests/Nordmann.LUI.Integration.Tests/Catalog/IndexDefinitionTests.cs` (new):
    `Index_of_an_embedded_definition_returns_an_embedded_handle_without_type_arguments`,
    `Index_of_a_linked_definition_returns_a_linked_handle`, `Create_again_with_the_same_definition_opens_the_index`,
    `Create_with_another_stored_definition_throws_a_mismatch_naming_the_fields`,
    `Create_of_several_definitions_is_all_or_nothing`, `Create_of_a_duplicate_name_throws_before_any_write`,
    `Changing_only_the_similarity_raises_no_mismatch`, `Changing_a_compared_field_raises_a_mismatch` (a theory over the
    fields), `Create_of_a_linked_index_syncs_nothing_and_installs_no_triggers`,
    `The_old_path_and_the_definition_store_the_same_definition`, `A_builders_Create_throws`,
    `An_invalid_definition_throws_a_configuration_exception_on_first_use_not_at_field_initialization`,
    `The_builder_function_runs_once_under_concurrent_first_use`,
    `An_operation_on_a_dropped_index_throws_index_not_found`, `An_operation_on_a_disposed_catalog_throws_object_disposed`.
  - `tests/Nordmann.LUI.Integration.Tests/Embedded/TypedHandleTests.cs` (new): `Upsert_and_search_with_long_keys`,
    `Sync_of_an_async_source_returns_the_report_of_the_same_entries` (criterion 4 of the addendum),
    `Hit_key_is_a_long`.
  - `tests/Nordmann.LUI.Architecture.Tests/PublicApiShapeTests.cs`: new `The_typed_handles_have_exactly_the_members_of_the_release`
    (the expected member signatures of every new type as a sorted string list — the target that every later PR extends);
    new `No_builder_holds_a_catalog` (no field of type `LuiCatalog` on a builder);
    new `No_type_outside_LUI_can_derive_from_the_definition_base` (no public/protected constructor);
    restate `The_catalog_creates_an_index_and_opens_one_that_is_there` (the old single `Create`/`Index` + the new
    overloads) and `The_results_and_options_have_named_properties_not_positions` (`LuiHit` and `LuiResult` now match two
    types each by name — count the generic forms in, and add them to the check).
  - `The_public_api_is_synchronous` stays unchanged and green.
- **Verification:** the full list; the mutation shards that cover `PublicApiShapeTests` and the builders.
- **Risks / traps:** the biggest PR of the run — keep the old path behaviour byte-equal (the integration suite must stay
  green without edits). `LuiHit<TKey>` with a positional constructor fails the named-properties guard. A `static readonly`
  definition must not throw in a static initializer (lazy validation). `docs/api.md` must get every new public type and
  member or `PublicApiDocumentationTests` fails. Mutations of `tools/mutation-check.sh` that `sed` the builders or
  `LuiCatalog.cs` (e.g. around `Create()`) must still match.
- **Stop if:** keeping the old path as a thin layer over `CreateCore` changes a stored definition of the old path
  (`SchemaGoldenTests` turns red) — the owner decides whether the golden file may change.

### 4. #269 Payload variants, typed linked builder (ADR-0073)

- **Approach:** add `LuiIndexDefinition<TKey, TPayload>`, `LuiLinkedIndexDefinition<TKey, TPayload>`, the handles
  `LuiIndex<TKey, TPayload>`, `LuiEmbeddedIndex<TKey, TPayload>`, `LuiLinkedIndex<TKey, TPayload>`, `LuiEntry<TKey,
  TPayload>` (null payload → `ArgumentNullException`; `Payload { get; }` without `init`), `LuiQuery<TKey, TPayload>`,
  `LuiResult<TKey, TPayload>`, `LuiHit<TKey, TPayload>` (no `Payload` yet — #262). `WithPayload<TPayload>(columns, map)`
  returns the new `LuiLinkedIndexBuilder<TPayload>` (not derived from `LuiLinkedIndexBuilder`; same option methods). The old
  path gets `CreateLinked<TPayload>(string, Func<LuiIndexKindBuilder, LuiLinkedIndexBuilder<TPayload>>)` so existing calls
  keep compiling. Writes serialize with today's `LuiCatalogOptions.PayloadSerializer` (no serializer →
  `LuiConfigurationException`). No payload-less write on a payload handle; `Upsert(key, text, payload, weight, priority, tx)`;
  synchronous `Sync(IAsyncEnumerable<LuiEntry<TKey, TPayload>>, …)` (addendum).
- **Where:** new `…OfKeyAndPayload.cs` files beside the ones of #261; `LuiLinkedIndexBuilder.cs` (+ new
  `LuiLinkedIndexBuilderOfPayload.cs`); `LuiCatalog.cs` (the `CreateLinked<TPayload>` bridge, the overloads of
  `Create`/`Index`). Model for the map: `Querying/ColumnPayload.cs` and `LuiCatalog.LinkedIndex<TPayload>` (lines 599–616).
- **Tests:**
  - `tests/Nordmann.LUI.Integration.Tests/Embedded/TypedPayloadHandleTests.cs` (new):
    `Index_of_a_payload_definition_returns_a_payload_handle`, `Typed_writes_store_the_payload` (read back through
    `catalog.Index(name)` + `hit.GetPayload<T>()`), `A_null_payload_throws`, `An_entry_with_cannot_set_a_null_payload`,
    `With_an_int_payload_Upsert_1_x_5_writes_the_payload_5`, `A_typed_write_without_a_serializer_throws_a_configuration_exception`.
  - `tests/Nordmann.LUI.Integration.Tests/Linked/TypedLinkedPayloadTests.cs` (new): `WithPayload_and_options_in_any_order`,
    `Two_definitions_with_the_same_view_type_use_their_own_maps`, `The_old_CreateLinked_with_WithPayload_still_works`.
  - Compile-time rules (a definition with a mismatching map does not compile; a payload handle has no payload-less write):
    in `PublicApiShapeTests.The_typed_handles_have_exactly_the_members_of_the_release` (the member list) — not as a
    compiling test.
- **Verification:** the full list.
- **Risks / traps:** `WithPayload`'s return type changes — every caller of the old `CreateLinked(name, i => …WithPayload(…))`
  in tests and sample must still compile through the new bridge overload; overload resolution between
  `CreateLinked(name, Func<…, LuiLinkedIndexBuilder>)` and `CreateLinked<TPayload>(…)` must pick the right one (write the
  test first). `LuiHit<TKey, TPayload>` does not derive from `LuiHit<TKey>`.
- **Stop if:** the bridge overload is ambiguous for existing callers and a caller would have to change before #268.

### 5. #270 Key types `int`, `string`, `Guid`; LUI format version 2 (ADR-0073)

- **Approach:** open `KeyTypes.Validate` to `long`, `int`, `string`, `Guid` (linked: `long`, `int`; `string`/`Guid` on a
  linked definition → `LuiConfigurationException` naming #267). The stored definition names the exact key type (`long`,
  `int`, `string`, `guid`); `KeyCodec` gets a per-type conversion (`Guid` as `"D"`, lower case; `int` invariant culture). New
  public enum `LuiKeyType` (`Long`, `Int`, `String`, `Guid`) and `LuiKeyException`. `Meta.SchemaVersion` 1 → **2**;
  opening a catalog whose `lui_meta` says format 1 (or a newer one) throws `LuiFormatException` with the message of the
  issue — at **open** time (`LuiCatalog.OpenCatalog` / `IndexStore.Verify`), not at index time. A linked `int` index leaves
  rows with keys outside the `int` range out of `Sync`/`Refresh` and counts them in `KeyOutOfRange`.
- **Where:** `src/Nordmann.LUI/Schema/KeyCodec.cs`, `Schema/Meta.cs` (line 7), `Indexes/IndexStore.cs` (`Verify`,
  `ReadIndex`), `LuiCatalog.cs` (`ReadEntry`, lines 631–669: the key-type check), `Indexes/LinkedIndex.cs` (the range check
  beside the document-id check), new `LuiKeyType.cs`, `LuiKeyException.cs`, `LuiFormatException.cs`.
- **Tests:**
  - `tests/Nordmann.LUI.Unit.Tests/Keys/KeyCodecTests.cs`: `A_guid_key_is_stored_canonically_in_lower_case_D_form`,
    `An_int_key_round_trips`, `A_damaged_stored_key_throws_a_key_exception_naming_index_and_key`.
  - `tests/Nordmann.LUI.Integration.Tests/Catalog/KeyTypeTests.cs` (new): theory `Embedded_keys_of_every_allowed_type_round_trip`
    (`long`, `int`, `string`, `Guid`), `A_linked_int_index_works`, `Opening_with_another_key_type_throws_a_mismatch`
    (including `int` against `long`), `Another_key_type_throws_a_configuration_exception_naming_the_allowed_types`,
    `A_linked_string_or_guid_definition_names_issue_267`, `A_linked_int_index_leaves_keys_outside_the_int_range_out_and_counts_them`.
  - `tests/Nordmann.LUI.Integration.Tests/Catalog/FormatVersionTests.cs` (new): `A_file_of_format_1_is_rejected_on_open_with_the_message`,
    `A_file_of_a_newer_format_is_rejected_on_open` (write `lui_meta.schema_version` by hand in a test database — the
    test is the one place that may).
  - `SchemaGoldenTests`: the golden schema/definition changes with the format — update the golden file in the green commit
    and say why in the commit body.
- **Verification:** the full list.
- **Risks / traps:** every existing test database is created fresh, so format 2 is invisible to them — but the golden test
  and any test that reads `lui_meta` will move. `LuiKeyKind` stays until #268. Do not name it "schema version" in new text:
  "LUI format version" vs "SQLite schema counter" (#265).
- **Stop if:** an existing test would need a format-1 file to pass.

### 6. #265 Definitions and index runtime in the catalog (ADR-0075)

- **Approach:** the catalog reads all stored definitions once at open (`IndexStore.ReadIndexNames` + `ReadIndex`) into a
  thread-safe map (`ConcurrentDictionary<string, StoredIndex>`); `Create` and `DropIndex` update file and map. The runtime of
  an index (the source, the rules, `QueryRunner` with `CandidateReader`, `TypoCorrection`, `HitReader`, `EmbeddedSync`,
  `EmbeddedCheck`, the typo dictionary) moves out of the handle into a per-catalog cache keyed by (index name, closed
  generic type); a handle holds catalog, definition, runtime. `Index(definition)` returns the **same handle instance** per
  definition object (`ConditionalWeakTable<LuiIndexDefinitionBase, object>` or a `ConcurrentDictionary` keyed by reference),
  doing the comparison once. **The mechanism (addendum):** every operation of a handle reads `PRAGMA schema_version` on its
  own connection before its first statement and compares it with the catalog's last-seen value (an `Interlocked` long); a
  change reloads the map once; a gone definition → `LuiIndexNotFoundException`, a changed one →
  `LuiDefinitionMismatchException`, and the cached handle and runtime are dropped. An unknown name reloads once and is
  remembered as missing until the counter changes. `Create` takes the write lock (`BEGIN IMMEDIATE`), re-reads `lui_meta`
  inside the transaction, and creates / opens / throws.
- **Where:** `LuiCatalog.cs`, `Indexes/IndexStore.cs`, `Indexes/EmbeddedIndex.cs` (438 lines — the runtime part becomes a
  new internal `Indexes/IndexRuntime.cs`), `Indexes/LinkedIndex.cs`, `Storage/Database.cs` (a helper
  `ReadSchemaCounter(SqliteConnection)`), the handles of #261/#269. Benchmarks: new
  `benchmarks/Nordmann.LUI.Benchmarks/OpenBenchmarks.cs` (`Index_of_a_definition`, `Query_with_a_handle_per_request`,
  `Query_with_a_held_handle`; `[MemoryDiagnoser]`; model `QueryBenchmarks.cs`).
- **Tests:**
  - `tests/Nordmann.LUI.Integration.Tests/Catalog/ThinHandleTests.cs` (new): `Index_of_a_definition_allocates_nothing_after_the_first_call`
    (`GC.GetAllocatedBytesForCurrentThread`, model `Catalog/WriteAllocationTests.cs`),
    `Index_of_a_definition_creates_no_command_after_the_first_call` (count `SqliteCommand`s — a counting hook in `Database`
    behind an internal test seam, or a `DiagnosticListener`; pick the one `QueryPlanTests` style allows),
    `Index_returns_the_same_instance_for_the_same_definition`,
    `A_query_with_a_handle_per_request_allocates_as_one_with_a_held_handle`,
    `An_unknown_name_reloads_once_then_throws_and_does_not_reload_again_while_the_counter_is_unchanged`.
  - `tests/Nordmann.LUI.Integration.Tests/Concurrency/TwoCatalogsTests.cs` (new): `An_index_created_by_one_catalog_is_opened_by_the_other`,
    `After_a_drop_and_a_recreate_by_one_catalog_the_old_handle_of_the_other_throws`,
    `A_new_open_after_a_recreate_sees_the_new_definition`, `Two_concurrent_Creates_with_the_same_definition_leave_one_index`.
- **Verification:** the full list; **A/B** `QueryBenchmarks` (v0.3.0 vs head, same session) against the budget of #274;
  the new `OpenBenchmarks` numbers in the PR body and in `docs/benchmarks.md` ("Opening an index (issue #265)").
- **Risks / traps:** `PRAGMA schema_version` costs a statement per operation — the short queries (`ka`, ~24 µs) are where
  +3 % is tight. The read path uses a **read-only** connection (ADR-0067): the pragma works there. The typo dictionary is
  already per catalog (`TypoDictionaries`); do not duplicate it in the runtime. "SQLite schema counter" vs "LUI format
  version" — name them apart in code, ADR and docs.
- **Stop if:** the A/B numbers miss the budget of #274 because of the check (addendum) — report, do not tune by removing
  the check.

### 7. #260 Payload registry, type and format fingerprint (ADR-0072)

- **Approach:** new `LuiPayloadRegistry` (`Use(JsonSerializerContext)`, `Use<T>(ILuiPayloadSerializer<T>)`), new generic
  `ILuiPayloadSerializer<TPayload>` with `Format`, `LuiPayloadSerializer.Json(context)` gets a typed form with format
  `"json-strict-v1"`, `[LuiPayloadName]` attribute, a stable computed name (`Schema/PayloadTypeName.cs`: namespace-qualified,
  `Outer+Inner`, generics without arity with their arguments, arrays `[]`). `LuiCatalogOptions.Payloads`; setting both
  `Payloads` and `PayloadSerializer` → `LuiConfigurationException`. The catalog copies (freezes) the registry at open. The
  lookup of `TPayload` happens once per catalog and definition (the cache of #265); unregistered → `LuiConfigurationException`
  at `Create`/`Index`; two contexts knowing a type → `LuiConfigurationException` naming both. `Create` of an embedded payload
  definition stores type name and format in the definition (compared fields); linked indexes store neither. The old untyped
  read path resolves `T` through the registry when `Payloads` is set.
- **Where:** new `LuiPayloadRegistry.cs`, `ILuiPayloadSerializerOfPayload.cs` (the generic interface),
  `LuiPayloadNameAttribute.cs`, `Schema/PayloadTypeName.cs`; changed `LuiPayloadSerializer.cs`, `LuiCatalogOptions.cs`,
  `Schema/IndexDefinition.cs` (two optional fields), `LuiCatalog.cs`, the payload handles of #269.
- **Tests:**
  - `tests/Nordmann.LUI.Unit.Tests/Payload/LuiPayloadRegistryTests.cs` (new): `The_registry_returns_the_serializer_of_a_type`,
    `Use_of_T_wins_over_a_context`, `A_second_Use_of_the_same_T_throws`, `Two_contexts_that_know_a_type_throw_naming_both`,
    `A_change_after_the_catalog_was_opened_has_no_effect`.
  - `tests/Nordmann.LUI.Unit.Tests/Payload/PayloadTypeNameTests.cs` (new): `The_attribute_name_survives_a_rename`,
    theory `Computed_names_are_stable` (non-generic, nested, generic, array; no assembly name or version in the result).
  - `tests/Nordmann.LUI.Integration.Tests/Embedded/PayloadFingerprintTests.cs` (new):
    `An_unregistered_type_throws_a_configuration_exception_on_create_and_on_index`,
    `Two_indexes_in_one_catalog_use_two_formats`, `Another_payload_type_throws_a_mismatch_naming_both`,
    `The_same_type_in_another_format_throws_a_mismatch_naming_both_formats`,
    `An_index_with_a_payload_type_cannot_be_opened_without_one_and_vice_versa`,
    `A_linked_payload_index_needs_no_registry_and_stores_no_type`, `The_old_read_path_resolves_through_the_registry`,
    `Setting_Payloads_and_PayloadSerializer_throws`.
  - `PublicApiShapeTests`: restate **`The_retired_names_are_gone_from_the_public_surface`** — it forbids a type named
    `ILuiPayloadSerializer\`1` (ADR-0035); ADR-0072 brings it back on purpose, so take that name out of the list with a
    comment citing ADR-0072. Restate `The_index_knows_no_types` only as far as `PayloadSerializer` still has its type (it
    stays until #268).
- **Verification:** the full list; the mutation that forbids the typed serializer must be adjusted, not deleted.
- **Risks / traps:** a context cannot list its types — resolution is `TryGetTypeInfo` per needed type, at `Create`/`Index`.
  The strict JSON serializer is the one of today (`UnmappedMemberHandling.Disallow`, required constructor parameters).
- **Stop if:** an existing payload test only passes with a catch-all serializer — "no catch-all" is the owner's rule.

### 8. #262 `hit.Payload` loaded with the query, `CheckPayloads()` (ADR-0074)

- **Approach:** `LuiHit<TKey, TPayload>.Payload` (get-only) set when the result is built: embedded — the payload bytes of
  the hits are read with the hit rows and deserialized before `Search`/`Execute` returns; linked — the map runs per hit. Any
  failure (serializer throws, map throws, map returns null) → `LuiPayloadException` naming index, key, type. A query
  without payload allocates nothing for payloads. `CheckPayloads(ct)` → `LuiPayloadCheck<TKey>` (`Unreadable`, `IsHealthy`)
  of `LuiUnreadablePayload<TKey>` (`Key`, `Message`), a full read. `Check()` reads no payload. The payload-change section of
  the docs with the duration rule (entries ÷ `UpsertMany` rate: 26,052/s in `docs/benchmarks.md`, "The batch").
- **Where:** `Querying/HitReader.cs` (it already reads hit rows; add the payload column for payload runtimes only),
  `Querying/PayloadSource.cs`, the payload handles, new `LuiPayloadCheck.cs`, `LuiUnreadablePayload.cs`; benchmark: new
  case(s) in `benchmarks/Nordmann.LUI.Benchmarks/QueryBenchmarks.cs` or a new `PayloadQueryBenchmarks.cs` (20 hits, a
  record of 3–5 fields, ~200 bytes JSON, the reference corpus; with and without payload).
- **Tests:**
  - `tests/Nordmann.LUI.Integration.Tests/Querying/HitPayloadTests.cs` (new): `Payload_is_set_when_the_query_returns_and_reading_it_calls_nothing`
    (a counting serializer and a counting map: exactly one call per hit, during the query),
    `An_unreadable_payload_fails_the_query_naming_index_key_and_type`, `A_throwing_map_fails_the_query`,
    `A_map_returning_null_fails_the_query`, `Payload_can_be_read_after_the_catalog_is_disposed`,
    `A_query_without_payload_allocates_nothing_for_payloads` (model `Querying/AllocationPoolTests.cs`).
  - `tests/Nordmann.LUI.Integration.Tests/Maintenance/CheckPayloadsTests.cs` (new): `CheckPayloads_lists_every_unreadable_entry_by_key`,
    `After_a_rewrite_the_entry_is_gone_from_the_list_and_the_query_succeeds`, `Check_reads_no_payload`,
    `A_removed_field_fails_old_entries_until_rewritten`, `A_field_added_with_a_default_keeps_old_entries_readable`,
    `The_new_index_way_works_without_a_failing_query`.
  - Mutation `the-hit-gets-a-payload-property` (`tools/mutation-check.sh` ~line 460): update it for the new rule (the
    untyped `LuiHit` still has no `Payload` property; the payload hit has one) — keep it biting.
- **Verification:** the full list; the payload benchmark (p50 ≤ 60 KB, p95 ≤ 100 KB, median ≤ +15 % over the same query
  without payload); **A/B** `QueryBenchmarks` (addendum).
- **Risks / traps:** reading payload bytes for every hit must not touch the no-payload runtime (budget +3 %). `MaxResults`
  bounds the work (≤ 1000). The ADR withdraws ADR-0035's "a payload never read costs nothing" — say so.
- **Stop if:** the payload query misses its allocation budget — the budget is the owner's (ADR-0032).

### 9. #272 State of a linked index: `NeedsInitialSync`, idempotent `CreateTriggers()` (ADR-0077)

- **Approach:** a stored flag `needs_initial_sync` per linked index in `lui_meta` (part of format 2), set to true by
  `Create`, set to false in the same transaction that commits a complete `Sync()` (today: the single transaction of
  `LinkedIndex.SyncAll`), **read from the file on every access** (not from the map of #265). `CreateTriggers()`: read
  `Triggers()`; `None` → create, `Stale` → drop and create, `Current` → nothing. `Create` checks that the table exists and
  installs nothing.
- **Where:** `Indexes/LinkedIndex.cs` (lines 232–260: `Sync`, `SyncAll`; the trigger methods), `Schema/Meta.cs`,
  `Indexes/IndexStore.cs`, the linked handles (`NeedsInitialSync` property).
- **Tests:** `tests/Nordmann.LUI.Integration.Tests/Linked/LinkedStateTests.cs` (new): `NeedsInitialSync_is_true_after_Create`,
  `It_stays_true_after_a_cancelled_sync`, `It_stays_true_after_a_failing_sync`, `It_becomes_false_after_a_complete_sync`,
  `It_survives_a_reopen`, `A_second_catalog_sees_false_right_after_the_first_completed_the_sync`;
  `tests/Nordmann.LUI.Integration.Tests/Linked/LinkedTriggerTests.cs`: `CreateTriggers_creates_on_None`,
  `CreateTriggers_replaces_on_Stale`, `CreateTriggers_does_nothing_on_Current`, `Create_of_a_linked_index_installs_no_triggers`;
  `PublicApiShapeTests.The_typed_handles_have_exactly_the_members_of_the_release` gets `NeedsInitialSync`.
- **Verification:** the full list.
- **Risks / traps:** the old `ILuiLinkedIndex.CreateTriggers()` may throw today on existing triggers — tests that expect
  the throw change; find them (`grep -rn CreateTriggers tests`) and list them in the PR.
- **Stop if:** an existing test asserts the old throwing behaviour as a documented promise of an ADR.

### 10. #271 Linked `Sync()` in batches, generation, incremental merge, lease (ADR-0077)

- **Approach:** replace `SyncAll`'s clear-and-refill in one transaction (`_source.ClearSql`, `LinkedIndex.cs:248`) by:
  take the lease (`lui_meta`: owner = a random id per catalog instance, expiry now + 60 s; a valid foreign lease →
  `LuiSyncInProgressException`; an expired one is taken over); next generation number; read the application's table in key
  order, `BatchSize` rows per batch, each batch indexed (insert or replace by key, `gen = current`) and committed in its own
  transaction, the lease renewed per batch; `automerge` off during the run and back to its default before each commit; at
  the end `DELETE … WHERE gen <> $current` in batches; then FTS5 `INSERT INTO <fts>(<fts>, rank) VALUES('merge', N)` in
  its own transactions until it reports nothing left (choose `N` so one step is about one batch — measure and write the
  number down); set `NeedsInitialSync = false`; rebuild a built typo dictionary once; release the lease in `finally`.
  `LuiLinkedSyncReport` gets `Batches`, `Removed`, `LongestLock`. The `gen INTEGER NOT NULL` column with an index on LUI's
  table of the linked index. Model: the embedded sync (`Indexes/EmbeddedSync.cs`, ADR-0016) — batches, automerge, cancel
  keeps committed batches.
- **Where:** `Indexes/LinkedIndex.cs`, `Schema/LinkedSource.cs` (the DDL and the SQL of the source), `Schema/Meta.cs`,
  `LuiLinkedSyncReport.cs`, new `LuiSyncInProgressException.cs`; `.agents/adr/0016-…` gets a note that the linked sync
  follows its rules; measurement: `tests/Nordmann.LUI.Performance.Tests/LinkedPerformance.cs` (the existing
  `The_write_lock_of_a_linked_sync` is the model and the "before").
- **Tests:** `tests/Nordmann.LUI.Integration.Tests/Linked/LinkedBatchSyncTests.cs` (new):
  `A_search_during_the_sync_never_sees_an_empty_index`, `After_the_sync_the_index_holds_exactly_the_rows_of_the_table`,
  `A_cancelled_sync_keeps_its_batches_leaves_NeedsInitialSync_true_and_releases_the_lease`,
  `A_row_deleted_before_the_sync_is_gone_after_it`, `A_row_deleted_during_the_sync_is_gone_after_ApplyPending`,
  `A_row_deleted_during_the_sync_without_triggers_is_gone_after_the_next_sync`,
  `A_second_concurrent_sync_throws_sync_in_progress`, `An_expired_lease_is_taken_over`, `The_merge_runs_in_steps`;
  performance layer: `A_writer_beside_a_linked_sync_of_a_million_rows_never_gets_busy` (busy timeout = 2 × the measured
  batch duration; during batches, removal and merge).
- **Verification:** the full list; the performance test of 1,000,000 rows run once in the background; **before/after** of
  the sync total in the **same session** (the `v0.3.0` worktree for "before", addendum) — within +15 %, `LongestLock`
  reported; the numbers in `docs/benchmarks.md` ("The batched linked sync (issue #271)").
- **Risks / traps:** the lease is a row in `lui_meta` — writing it needs the write lock, keep that write short. The busy
  test is a timing test: give it the trait and the argued bounds of ADR-0050 (`TimingTraitTests`). FTS5 `'merge'` returns
  "nothing left" through `changes()` — check the SQLite docs behaviour in a unit test first.
- **Stop if:** the total exceeds +15 % or a writer still gets `SQLITE_BUSY` — report the numbers; the budget is the owner's.

### 11. #273 Maintenance by name (ADR-0076)

- **Approach:** on `LuiCatalog`: `Indexes()` → `IReadOnlyList<LuiIndexInfo>` (definition fields from the map of #265, state
  fields read on call), `Count(name)`, `Check(name)` (structure only), `Optimize(name)`, `Rebuild(name)` (embedded only;
  linked → `InvalidOperationException` with the message of the issue). They open the internal runtime by the stored
  definition, never a typed handle, and read no payload and call no map. A missing name: reload once, remembered as missing
  until the SQLite schema counter changes (the mechanism of #265).
- **Where:** `LuiCatalog.cs` (next to `DropIndex` and `WarmUpTypoDictionaries`), new `LuiIndexInfo.cs`; reuse
  `LuiIndexSource` (from #261) and `LuiKeyType` (from #270).
- **Tests:** `tests/Nordmann.LUI.Integration.Tests/Maintenance/MaintenanceByNameTests.cs` (new): theory
  `Indexes_Count_Check_Optimize_work_for_every_kind` (embedded/linked × with/without payload, no definition in the calling
  code), `Rebuild_works_for_an_embedded_index`, `Rebuild_of_a_linked_index_throws_with_the_message`,
  `Indexes_executes_no_SQL_for_the_definition_fields`, `Check_by_name_reads_no_payload_and_calls_no_map`,
  `A_missing_name_asked_twice_reloads_once`.
- **Verification:** the full list.
- **Risks / traps:** `Indexes()` must not reach into the typed handle cache (it would need `TPayload`). The ADR-0076 file
  is created here (its first issue); #266 adds its sections.
- **Stop if:** —

### 12. #266 `catalog.Initialize()`, `AddLuiOutbox`, the host pattern (ADR-0076)

- **Approach:** `Initialize(LuiInitializeOptions?, ct)` → `LuiInitializeReport`; the order of the issue, step by step
  across the selected indexes, each step only when its state needs it (`Triggers()`, `NeedsInitialSync`, `Pending()`,
  `TypoDictionaryState`); the initial sync goes through the lease of #271 and a held foreign lease is reported as
  `InitialSyncRunningElsewhere`; dictionaries last, one after the other, and only for an index without a pending file step;
  failures collected per index, `LuiInitializeException` at the end with `Report` and `Errors`; cancellation between steps.
  `LuiInitializationNeeds` (`[Flags]`) on `LuiIndexInfo.NeedsInitialization`. Read-only catalog: dictionaries only, file
  steps reported. Hosting: `AddLuiOutbox(TimeSpan)` / `AddLuiOutbox(Action<LuiOutboxOptions>)` — a `BackgroundService` that
  calls `ApplyPending()` every `Interval`, logs a failing round as a warning, backs off exponentially to `MaxBackoff`, resets
  on success, throws at start on a read-only catalog. Fix the doc comment of `LuiTypoDictionaryState.NotBuilt`.
- **Where:** `LuiCatalog.cs`, new `LuiInitializeOptions.cs`, `LuiInitializeReport.cs`, `LuiInitializeProgress.cs`,
  `LuiInitializeException.cs`, `LuiInitializationNeeds.cs`, `LuiIndexInfo.cs`, `LuiTypoDictionaryState.cs`;
  hosting: `src/Nordmann.LUI.Hosting/LuiServiceCollectionExtensions.cs`, new `LuiOutboxOptions.cs`, new
  `Hosting/LuiOutboxService.cs` (model `Hosting/LuiWarmupService.cs` — its round-as-a-task fix of #185 is the pattern for
  stopping cleanly).
- **Tests:**
  - `tests/Nordmann.LUI.Integration.Tests/Catalog/InitializeTests.cs` (new): `Initialize_with_defaults_brings_three_indexes_to_ready_in_order`,
    `A_second_Initialize_does_nothing`, `Each_option_switches_its_step_off`, `Indexes_limits_the_run`,
    `A_definition_of_a_missing_index_throws_before_any_step`, `One_failing_index_does_not_stop_the_others`,
    `A_run_cancelled_during_a_sync_keeps_what_it_finished`, `The_dictionaries_are_built_one_after_the_other`,
    `On_a_read_only_catalog_only_the_dictionaries_are_built`, `Without_Initialize_the_needs_are_reported_and_nothing_was_done`.
  - `tests/Nordmann.LUI.Integration.Tests/Concurrency/TwoCatalogsInitializeTests.cs` (new):
    `Exactly_one_of_two_catalogs_runs_the_first_sync_the_other_reports_it_running_elsewhere`.
  - `tests/Nordmann.LUI.Integration.Tests/Hosting/OutboxTests.cs` (new): `A_failing_round_is_logged_and_retried_with_backoff`,
    `The_first_successful_round_returns_to_the_interval`, `On_a_read_only_catalog_AddLuiOutbox_throws_at_start`
    (model `Hosting/LuiWarmupTests.cs`, `RecordingLoggerProvider.cs`).
  - `tests/Nordmann.LUI.Integration.Tests/Hosting/HostPatternTests.cs` (new): `A_broken_definition_fails_the_start_at_Create`,
    `With_the_background_initializer_ready_is_false_until_Initialize_is_done`,
    `A_crash_after_Create_and_a_second_start_leave_every_index_ready`.
  - `PublicApiShapeTests.The_container_holds_the_catalog_and_the_warm_up_but_no_index`: `registrations.Length` 2 → 4
    (`AddLui`, `AddLuiWarmup`, two `AddLuiOutbox`) until #268 — restate it with a comment.
- **Verification:** the full list.
- **Risks / traps:** timing tests (backoff, readiness) need the trait and argued bounds of ADR-0050 and must not poll
  briefly (the flake of #154/#185: a claim must stay true). No hosted service that calls `Initialize()` in LUI.
- **Stop if:** the host pattern test needs ASP.NET Core in the hosting package — the package takes no web dependency
  (ADR-0018); use a generic host in the test project.

### 13. #268 Remove every replaced member, move every caller (ADR-0073)

- **Approach:** delete every row of the removal table of #258 (with the addendum: the old interfaces go with their
  `Sync(IAsyncEnumerable<LuiEntry>)`; the new handles keep their own synchronous one). Move every caller in `src/`,
  `tests/`, `benchmarks/`, `samples/`, `tools/` to definitions + `catalog.Index(definition)`; keep what each test proves. Rename
  `The_index_knows_no_types` → `The_index_layer_references_no_application_types` and restate it (the index and storage
  layers reference no application type and no serializer). Update every mutation in `tools/mutation-check.sh` whose target
  moved. The CHANGELOG migration line for every row (and the two "changed" rows).
- **Where:** the root files of the removed types (`ILuiIndex.cs`, `ILuiEmbeddedIndex.cs`, `ILuiLinkedIndex.cs`,
  `LuiHit.cs`, `LuiResult.cs`, `LuiEntry.cs`, `LuiQuery.cs`, `LuiTypedIndex.cs`, `LuiTypedResult.cs`, `LuiTypedHit.cs`,
  `LuiIndexExtensions.cs`, `LuiKey.cs`, `LuiKeyKind.cs`, `ILuiPayloadSerializer.cs`), `LuiCatalog.cs`, `LuiCatalogOptions.cs`,
  the builders; hosting `LuiWarmupOptions.cs`, `Hosting/LuiWarmupService.cs`, `LuiServiceCollectionExtensions.cs`; all callers
  (`grep -rln "ILuiEmbeddedIndex\|LuiTypedIndex\|GetPayload\|CreateLinked\|LinkedIndex(\|AddLuiWarmup\|LuiKey\b" src tests benchmarks samples tools`).
- **Tests:** no test is deleted; the PR lists every test file it touched. The restated guard is seen red first (a commit
  that references an application type from `Indexes`, red, then reverted). `PublicApiShapeTests` confirms the removed
  members are gone (`The_retired_names_are_gone_from_the_public_surface` gets the removed names).
- **Verification:** the full list; **A/B** `QueryBenchmarks` (addendum), before/after in the PR.
- **Risks / traps:** the biggest diff of the run — do it in reviewable commits (one per group of the table). The sample
  moves minimally here (compile and its tests green); its showcase is #263. `docs/api.md` loses the rows of removed members
  (`The_page_names_no_type_that_the_code_does_not_have`).
- **Stop if:** a test can only be kept by changing what it proves.

### 14. #263 Sample and documentation

- **Approach:** the sample shows the typed API end to end with a `SearchIndexes` class, `[LuiPayloadName]` on every payload
  type, the registry, the host pattern of #266 (factory opens only; `Create` after `Build()`; the application's own
  background `Initialize()`; readiness on `NeedsInitialization`; `AddLuiOutbox`), the synchronous `Sync(IAsyncEnumerable)`
  for an asynchronous import (addendum), maintenance by name. README quick start and a before/after example (before =
  ```` ```text ````, after = ```` ```csharp ````; addendum). Tutorial `docs/tutorial/03-the-payload.md` with every point of
  the issue. `docs/api.md` complete. CHANGELOG `## [Unreleased]`: the migration notes of the whole release and "format 1 is
  rejected".
- **Where:** `samples/Lui.Sample/` (`Program.cs`, `SampleApp.cs`, `Indexes/`, `Data/`, `Api/`, `OutboxService.cs` → replaced
  by `AddLuiOutbox`), `samples/Lui.Sample.Tests/` (the testing pattern: a real catalog on a temporary file),
  `README.md`, `docs/api.md`, `docs/tutorial/03-the-payload.md` (and the other tutorial chapters that show the old API),
  `CHANGELOG.md`, `docs/testing.md`.
- **Tests:** `samples/Lui.Sample.Tests/SearchIndexesTests.cs` (new): `The_applications_search_works_against_a_real_catalog`;
  the package smoke test runs every README C# block; `tools/tutorial-check.py` compiles the tutorial blocks; a guard (in
  `PublicApiDocumentationTests` or a new `RemovedMembersTests`) that no removed member name appears in the docs outside
  `CHANGELOG.md` and the "before" halves (criterion 2 with the addendum).
- **Verification:** the full list, including `npm test` (the sample client) and `python3 tools/tutorial-check.py`.
- **Risks / traps:** `SampleClientTests` guards the sample client; the client of #212 (PR #220) is not part of this run —
  do not touch `wwwroot` beyond what the API change forces.
- **Stop if:** the README's measured numbers would need a new run of the reports — that is #275's.

### 15. #221 Align LUI with the Nordmann standards

- **Approach:** the steps of the issue, with its addendum: attach `nordmanndev/standards` read-only (`add_repo`), version
  `dev` (no tag exists; note the commit), `sync.py --profile library`, delete `release.yml` and `backend.yml` (after porting
  any missing check into `ci.yml`), the issue templates to Feature/Task/Bug, the labels `ready`/`blocked`/`prio:*`, decisions
  in one place (an ADR — the next free number after 0077), the rules into `.agents/agents.md` with `AGENTS.md` as the
  pointer (the round rule of 0.4.0 moves unchanged), the comparison of the starter files and the library canon listed in the
  PR, `standards.lock.yaml` committed.
- **Where:** `.github/`, `.agents/`, `AGENTS.md`, `docs/standards/`, `docs/adr/README.md` (removed or kept, per the ADR).
- **Tests:** `check_docs.py` 0 findings; a second sync leaves `git status` clean; every architecture guard that reads
  `AGENTS.md` or `.github/` (`WorkflowRulesTests`, `LinkCheckTests`, `AdrPathTests`) stays green.
- **Verification:** the full list plus the outputs the issue names.
- **Risks / traps:** label deletion only per the addendum (after the other issues are closed except #275 and #276, which
  are moved to the new labels first). `global.json`: the starter says `latestFeature`, LUI says `latestPatch` — a deliberate
  difference to list, not to change silently.
- **Stop if:** `nordmanndev/standards` cannot be attached, or the sync would change an existing file of LUI.

### 16. #275 Performance analysis of 0.4.0

- **Approach:** one session, the A/B rule: `QueryBenchmarks` `v0.3.0` vs `0.4.0` head alternating (≥ 2 runs each), the
  payload benchmark of #262, `OpenBenchmarks` of #265, and the linked sync of 1,000,000 rows (#271) before/after. Write
  "The budget of 0.4.0" in `docs/benchmarks.md` with the four budgets, the numbers, the limits, met/missed.
- **Where:** `docs/benchmarks.md`; if the README tables are refreshed, `tools/benchmarks.sh` for all five reports in one
  run.
- **Tests:** `ReadmeBenchmarksTests`, `ReadmeBudgetsTests`, `LuiBudgetsTests` green.
- **Verification:** the architecture tests; the raw numbers in the PR.
- **Risks / traps:** ~2–3 hours of measuring; nothing else may run.
- **Stop if:** a budget is missed — report it with numbers; never lower a limit (AGENTS.md).

### 17. #276 Release 0.4.0 into the branch

- **Approach:** `VersionPrefix` 0.4.0 with a comment citing ADR-0071; `ReleaseThreeTests.The_package_is_a_version_of_the_round_of_0_3`
  expects `0.4.` (rename it to say 0.4, comment ADR-0071), the other tests of that file read and kept or restated;
  `CHANGELOG.md` `[0.4.0] — <date set by the owner>` with a new empty `[Unreleased]`; README status and comparison links.
  Then open `0.4.0 → dev` with the list of the merged PRs of the round — **do not merge it**.
- **Where:** `Directory.Build.props` (line 40–41), `tests/Nordmann.LUI.Architecture.Tests/ReleaseThreeTests.cs`,
  `CHANGELOG.md`, `README.md`. Model: PR #244 and PR #249 of 0.3.0.
- **Tests:** the version guard red first (expect `0.4.` before the bump), then green.
- **Verification:** the full list; `dotnet pack` lists `Nordmann.LUI.0.4.0.nupkg` and `Nordmann.LUI.Hosting.0.4.0.nupkg`.
- **Risks / traps:** the trap of the release (`.agents/state/active-task.md`): the button "Delete branch" after a merge
  whose head is `dev` deletes `dev` — this run never merges into `dev`.
- **Stop if:** always, at the end: the release is the owner's.
