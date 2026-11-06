# Active task

<!--
What the next session is doing. One task, in the words of the issue that
describes it. Link the issue; do not repeat the whole plan here.
-->

**Issue:** [#56](https://github.com/nordmanndev/LUI/issues/56) — the read path: an index that is **opened** and not created.
The catalog reads the definition out of `lui_meta` and writes nothing — no table, no definition, no write lock — so a read
replica, a file in the mode `ro` and a service that must not write can use LUI.
**Branch:** `feature/read-path-open` → the pull request **#57** is opened against `main`
(<https://github.com/nordmanndev/LUI/pull/57>).
**State:** the rebuild is on the same branch, PR #57 stays open and is **not** merged. The branch carries the five
commits of the first shape of the issue (`945729e` the read path, `dddecf1` the wording of the documents, `c7de0f3` the
test of the payload, `5049e52` a note, `2fb05ca` the fix of the distance table that the CI found) and three commits of
the rebuild on top of them (`fe3454c` a note, `37095f6` the plan and ADR-0033, `6e751f3` the rebuild itself) — no force
push. The CI on `6e751f3` is green, and the independent verification of the pull request is **PASS**
(`/workspace/verify-56-redesign.md`, read-only, plus a second verification `/workspace/verify-56-redesign-v2.md`).
The plan is `docs/plan/2026-11-05-issue-56-the-catalog-is-the-entry-point.md`, the decisions are
[ADR-0033](https://github.com/nordmanndev/LUI/blob/main/docs/adr/0033-the-read-path-opens-an-index-without-writing.md)
(the read path) and
[ADR-0034](https://github.com/nordmanndev/LUI/blob/main/docs/adr/0034-the-hosting-package-carries-only-the-dependency-injection.md)
(the hosting package).

**Redesign (2026-11-05, nach der Rueckmeldung des Owners):** die Form der API ist neu entschieden und
ADR-0033 ist umgeschrieben. Der Katalog ist der Einstiegspunkt und das Einzige im Container
(`AddLui`); das Erstellen ist ein Aufruf (`catalog.Create(name, builder)`), das Oeffnen ein anderer
(`catalog.Index<TKey, TPayload>(name, payload)`); es gibt **eine** Implementierung je Faehigkeit — die
untypisierte Fassade (`ILuiDocumentIndex`) und die Index-Registrierungen (`AddIndex`, `AddOpenIndex`)
entfallen; und die **Datei** entscheidet, was erlaubt ist: eine nur lesbare Verbindung
(`Mode=ReadOnly`) schreibt nicht und verweigert jeden Schreibvorgang mit `LuiReadOnlyException`.

## What is done

- **The library**: `LuiCatalog.Create<TKey, TPayload>(name, builder)` and `LuiCatalog.Create<TPayload>(name, builder)`
  create an index — the builder is the **parameter**, not the way in, and the builder is not reachable from the catalog any
  more. `LuiCatalog.Index<TKey, TPayload>(name, typeInfo)`, `Index<TKey, TPayload>(name, payload?)`,
  `Index<TPayload>(name, map)` and `Index(name)` open an index that exists and write nothing (a plain `SELECT` on
  `lui_meta`, no transaction, no write lock, no `CREATE TABLE`). The definition carries the **type of the payload** now
  (`IndexDefinition.PayloadType`): an index that stores `TagChip` is not opened with `RecipeChip`, and the
  `LuiDefinitionMismatchException` names both.
- **The file decides** (ADR-0033): a catalog on `Mode=ReadOnly` switches on no WAL, creates no `lui_meta` and refuses
  every write with the new `LuiReadOnlyException` — `Create`, `DropIndex`, `Upsert`, `Sync` and the rest.
- **The container holds the catalog and nothing else**: `AddLui` is the only registration; the registrations, the hosted
  services and the warm-up of the typo dictionaries are gone. The hosting package carries **only** the dependency
  injection — `Microsoft.Extensions.Hosting.Abstractions` and `.Logging.Abstractions` are out of it (ADR-0034).
- **The untyped facade is gone**: `ILuiDocumentIndex`, `LuiDocumentHit`, `DocumentIndex`, `DocumentSource`,
  `RawJsonPayload` and `ReadOnlyEmbeddedIndex` are deleted, with every reference to them.
- **The callers follow**: the sample, the tests and the benchmarks use `catalog.Create(…)` and `catalog.Index(…)`.
- **The documents**: spec §7.1, §12.1, §12.2, §12.3, §12.4, §12.7, §12.8, §12.9, §14, §15, §17.1, §17.2, §17.3, the
  README (quick start with `Create` and `Index`, the host section with `AddLui` only, "Only read"), the CHANGELOG with
  the migration list, `docs/adr/README.md` (ADR-0033 and ADR-0034) and `docs/testing.md`.

## Open

- The rebuild is **committed** and pushed — the documents, the library, the callers and the hosting package are in one
  commit on top of the five commits above (no force push), and PR #57 carries the body of the new shape.
- The pull request #57 waits for the merge — the owner merges it, the CI is green.
- The verification names four minor findings; the first one is worth closing before the release:
  `catalog.Index<TKey, TPayload>(name, null)` is ambiguous — the overload with `JsonTypeInfo<TPayload>` and the one with
  `ILuiPayloadSerializer<TPayload>?` both accept `null`. Every documented form compiles and works; only the explicit
  `null` literal fails. The other three are imprecisions in the pull request body (it counts 19 instead of 18 tests in
  `ReadPathTests`, and it lists `OpenIndexHostingTests.cs` as removed although the file never was on `main`) and the
  package smoke test, which fails in the sandbox on `main` as well — no regression.
- `SampleApiTests.A_request_the_page_abandons_stops_the_work_of_the_server` is **timing-sensitive** (the client aborts
  after 150 ms; the server has to notice the closed connection during a 1.5 s delay). It failed once under the load of a
  saturated 2-CPU sandbox; the changes of #56 are not on the path the test walks (the probe never opens an index), and
  it has been green in every run since. The test was not changed.
  `SampleApiTests.A_row_written_to_the_application_s_table_reaches_the_linked_index_through_the_triggers_and_the_outbox`
  is timing-sensitive too: it failed once under the same load (the outbox was not applied in time) and is green in
  isolation and in the next full run.
- The migration of the files of today: an index that was created by an older version has no payload type in its
  definition — it is dropped and created again (`DropIndex` + `Create`).

## The next step

After the pull request of #56 is merged: **#38** (the budget table of the README guarded against the specification of
§16), then the **release** (PR-012, `0.1.0`) — which the owner has to ask for; after that #35, #31, #32, #44 and the
`compactLevel` follow-up of #48.
