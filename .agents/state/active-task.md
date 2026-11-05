# Active task

<!--
What the next session is doing. One task, in the words of the issue that
describes it. Link the issue; do not repeat the whole plan here.
-->

**Issue:** [#56](https://github.com/nordmanndev/LUI/issues/56) — the read path: an index that is **opened** and not created.
The catalog reads the definition out of `lui_meta` and writes nothing — no table, no definition, no write lock — so a read
replica, a file in the mode `ro` and a service that must not write can use LUI.
**Branch:** `feature/read-path-open` → the pull request is opened against `main` (its number is in the report of the task
below).
**State:** implemented in the working tree, **not yet committed** — the sandbox's shell died for a long time in the middle of
the work (the worker sessions with it), so build, test, commit, push and the pull request are the next step. The plan is
`docs/plan/2026-11-05-issue-56-the-read-path.md`, the decision is
[ADR-0033](https://github.com/nordmanndev/LUI/blob/main/docs/adr/0033-the-read-path-opens-an-index-without-writing.md).

## What is done

- **The library**: `IndexStore.ReadIndex` reads the definition with a plain `SELECT` on a connection of its own (no
  transaction, no write lock, no `CREATE TABLE`); `LuiCatalog.OpenIndex(name)` gives the untyped facade
  (`ILuiDocumentIndex`, `LuiDocumentHit` — the key as the text it is stored as, the payload as the raw JSON),
  `LuiCatalog.OpenIndex<TKey, TPayload>(name, payload?)` the typed one, and every write through a typed, opened index is
  refused. The checks: the index exists (`LuiIndexNotFoundException`), the schema version and the fold version fit, the
  key type and the payload fit. 16 tests in `ReadPathTests` — one per acceptance point of the issue, including the red
  proof (with an open write transaction `OpenIndex` answers at once, `Create()` blocks) and the fingerprint of the file
  (not a byte changes).
- **`Ensure()` is now `Create()`** — a rename, the old name is gone (before 0.1.0, ADR-0026). The spec, the README, the
  CHANGELOG (with the migration list), the smoke test and the wording of the hosting package follow.
- **The container**: `AddOpenIndex(name)` and `AddOpenIndex<TKey, TPayload>(name, payload?)` register an index that the
  host **opens** instead of creating it; `AddIndex` keeps its meaning. `OpenIndexRegistration` is a type of its own — an
  opened index has no typo dictionary to warm up and no outbox to apply. Tests in `OpenIndexHostingTests`.
- The documents: spec §7.1 (the definition carries the payload columns), §12.9 (the read path), §14/§15 (the lifetime),
  the README (a section "Only read"), the CHANGELOG, `docs/testing.md`, the ADR.

## Open

- The build, the test run, the commit, the push and the pull request — the shell of the sandbox was dead
  (`CLOUD_BASH_SHELL_EXECUTION_FAILED`, `gateway_504`) for a long time; a session was started for it.
- `SampleApiTests.A_request_the_page_abandons_stops_the_work_of_the_server` is **timing-sensitive** (the client aborts
  after 150 ms; the server has to notice the closed connection during a 1.5 s delay). It failed once under the load of a
  saturated 2-CPU sandbox; the changes of #56 are not on the path the test walks (the probe never calls `OpenIndex`), and
  it has been green in every run since. The test was not changed.

## The next step

After the pull request of #56 is merged: **#38** (the budget table of the README guarded against the specification of
§16), then the **release** (PR-012, `0.1.0`) — which the owner has to ask for; after that #35, #31, #32, #44 and the
`compactLevel` follow-up of #48.
