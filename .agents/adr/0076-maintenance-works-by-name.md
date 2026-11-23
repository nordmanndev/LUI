# 0076 — Maintenance works by name: the catalog describes, counts, checks, optimizes and rebuilds an index by its name, without its definition or its payload type

- **Status:** Accepted (owner, 2026-11-20)
- **Date:** 2026-11-22
- **Builds on:** [0073](./0073-the-index-definition-and-the-typed-handles.md) (an index is opened only with its definition; the key types),
  [0075](./0075-the-catalog-holds-the-definitions-and-the-runtime-of-an-index.md) (the definitions in memory, the guard inside the snapshot of an operation, the missing name),
  [0072](./0072-the-serializer-is-bound-to-the-payload-type-through-a-registry.md) (the payload type and its format are stored),
  [0074](./0074-the-hit-carries-its-payload.md) (`CheckPayloads()` is the check that reads payloads),
  [0016](./0016-sync-check-rebuild.md) (sync, check, rebuild),
  [0068](./0068-three-ways-to-open-a-catalogue.md) (a catalog that may not be written refuses what writes)
- **Affects:**
  - `src/Nordmann.LUI/LuiCatalog.cs` — `Indexes()`, `Count(name)`, `Check(name)`, `Optimize(name)`, `Rebuild(name)` and the lookup they share
  - `src/Nordmann.LUI/LuiIndexInfo.cs` (new, #273) — the record that describes an index by name
  - `src/Nordmann.LUI/Indexes/IndexRuntimes.cs` — the runtime of an index by its stored definition alone
  - `src/Nordmann.LUI/Typos/TypoDictionaries.cs` — the state of a dictionary, asked without creating one
  - `docs/api.md`, `docs/testing.md`, `CHANGELOG.md`, `.agents/adr/README.md`
  - `tests/Nordmann.LUI.Integration.Tests/Maintenance/MaintenanceByNameTests.cs`, `tests/Nordmann.LUI.Architecture.Tests/PublicApiShapeTests.cs`, `tools/mutation-check.sh` — the rules and the guards
- **Issues:** [#273](https://github.com/nordmanndev/LUI/issues/273) (maintenance by name), part of the epic of 0.4.0 ([#258](https://github.com/nordmanndev/LUI/issues/258)); [#266](https://github.com/nordmanndev/LUI/issues/266) adds its sections to this record

## Context

Since #261 an index is opened with its definition, and an index with a payload type is opened with a handle that names the type
(ADR-0073). That is right for the application, which has its definitions. A tool, an admin endpoint or a health check — and later the
client of #212 — has none of them: it has a catalog and a name, and it must inspect and maintain **every** index of the file. If the only way
to count or check an index were a typed handle, such a tool would have to link every payload type of every application whose file it opens.

The owner decided on 2026-11-20: maintenance works by name, through the catalog, without definitions or payload types.

## Decision

1. **Five members of `LuiCatalog`**, for embedded and linked indexes alike, except where the table says otherwise:

   | member | embedded | linked |
   |---|---|---|
   | `IReadOnlyList<LuiIndexInfo> Indexes()` | ✓ | ✓ |
   | `long Count(string name)` | ✓ | ✓ |
   | `LuiIntegrityReport Check(string name)` | ✓ | ✓ |
   | `void Optimize(string name)` | ✓ | ✓ |
   | `void Rebuild(string name)` | ✓ | throws `InvalidOperationException` |

   `DropIndex(name, dropTriggers)` and `WarmUpTypoDictionaries(names)` are by name already and stay as they are. **None of the five returns a
   handle or can write an entry**, so "one index, one kind of definition" (ADR-0073) is untouched: a typed handle is still the only way
   to write an entry or to read a payload.
2. **The runtime is the stored definition's, and nothing of the application is in it.** An operation by name builds the internal runtime —
   the index, its source, its guard — from the stored entry of `lui_meta`, with no payload reader and no map: it **reads no payload and calls
   no map**. `Check(name)` is the structure only (the definition, the triggers, FTS5's integrity check, the counts against the table, and — for a linked
   index — that the table still has the **payload columns** of the stored definition: they are stored, so the check needs no map and
   no registry); whether
   the stored payloads can be read is `CheckPayloads()` of a typed handle (ADR-0074), which needs the type. The runtime is built for the call
   and let go; nothing of it is kept, so the handle cache of ADR-0075 — which needs a definition object — is never reached.
3. **The guards of ADR-0075 hold for every operation by name, with the same machinery.** The runtime has the `IndexGuard` of a handle: the
   schema counter is read inside the snapshot of a read and inside the write transaction of a write, so the check and the work are one step; an
   index that is gone is a `LuiIndexNotFoundException` that names it. The one difference is that a call by name **declares no definition**: an
   index that another process dropped and created again with another definition is not a `LuiDefinitionMismatchException` for the caller, it
   is the index as it is stored now. When the guard says that the definition it was built from is not the stored one, the call looks again from
   the definitions as the file holds them (`StoredIndexes.Refresh`) and is made again; it stops when the file stops moving, and then the
   exception of the guard is the answer. No exception is thrown from a snapshot that has not been compared with the file.
4. **A missing name** reloads the stored definitions once and is remembered as missing until the SQLite schema counter changes
   (`StoredIndexes.Find`, ADR-0075), so a name that a user types on an admin endpoint does not cause a reload per request. A name that is no
   index name at all (`^[a-z][a-z0-9_]{0,40}$`) is an `ArgumentException`, as everywhere.
5. **`Indexes()` is a list of records, read per field.** `LuiIndexInfo` is a sealed record with named properties: `Name`, `Source`
   (`LuiIndexSource.Embedded` or `Linked`; it is called `Source` because `Kind` already means single word or multi word), `Kind`, `PayloadType`
   and `PayloadFormat` (stored for an embedded index with a payload type, `null` otherwise — a linked index stores neither, ADR-0072), `KeyType`
   (`LuiKeyType`, the stored key type, ADR-0073), `TypoDistance` and `TypoDictionaryState`. #266 adds `NeedsInitialization`.
   - The **definition fields** come from the definitions in the memory of the catalog: **no statement reads them**. The one statement of the
     call asks whether the schema counter of the file moved, and only a move reads `lui_meta` again — so an index that another process created
     or dropped is in the list, and an index is never listed from a snapshot that the file has left.
   - The **state field** `TypoDictionaryState` is read at the call. The dictionary lives in the memory of the process, one per index and
     definition, so it is read from the dictionaries of this catalog — **without creating one**; a dictionary that was built for another
     definition of the same name is not the dictionary of the stored one, and the state is `NotBuilt`. It says what *this* catalog has built.
   - The list is in the order of the names. An index whose row this version cannot read (another LUI format, another folding, a key type it
     does not know, a kind it does not know, a definition of `null` or of malformed JSON) is a `LuiDefinitionMismatchException` that names
     it — the error of the parser is its inner exception — as `WarmUpTypoDictionaries()` already does, **for `Indexes()` and for every operation by
     name on that index**. `Indexes()` throws for one damaged row and does not list the others around it: a list with a hole would hide the one
     row the operator has to `DropIndex`, which is the way out and reads such a row leniently. The other indexes stay usable by name.
6. **`Rebuild(name)` is for an embedded index only.** For a linked index it throws an `InvalidOperationException` before it touches the file:
   *"'chefs' is a linked index; rebuild it from its table with Sync() on its handle."* A linked index has no content of its own to rebuild
   the search index from: its content is the application's table, and `Sync()` is the call that reads it. The refusal is thrown only from a
   definition that has been compared with the file, like every other.
7. **A catalog that may not be written** lists and counts. What writes — `Optimize` and `Rebuild` — is a `LuiReadOnlyException` that names
   the index (ADR-0068); so is `Check(name)`, whose integrity check of FTS5 is an insert that is rolled back, as `Check()` of a handle always was. For the same
   reason `Check(name)` **holds the write lock of the file for the whole integrity check**, which reads the whole index: it is cheap in what it
   reads, not in the lock it takes (#275 measures it), and a host should not call it from a probe of a second on a large index.

Which check for what:

| check | cost | use |
|---|---|---|
| `Check()` of a handle, `catalog.Check(name)` | structure only; holds the write lock while FTS5 checks the index, refused on a read-only catalog | health checks on a schedule |
| `catalog.Indexes()` → `NeedsInitialization` (#266) | cheap: state only | readiness, every probe |
| `CheckPayloads()` (ADR-0074) | reads every stored payload | maintenance, after a change of the payload type |

## Alternatives rejected

- **Handles by name** (`catalog.Index("chefs")` for a typed index, with the payload type as a generic argument at the call): it would make
  a second kind of definition for the same index — one in the application, one by name — and the tool would still need every payload type.
  The by-name operations return no handle; that is what keeps ADR-0073 intact.
- **Cache the runtime by name.** It would need an own invalidation next to the one of the handle cache (a drop, a recreate, a failed guard),
  to save the construction of an object that the operation by name — a count, a check, an optimize — costs much more than. The runtime is built
  for the call; the work is not.
- **`Indexes()` without the statement for the counter** (the snapshot as it is): free, and wrong as soon as another process creates an index —
  a tool that lists the indexes of a file that several processes write would list a stale file. One statement per call is the price of
  a list that is true, and no definition field costs a statement.
- **A count and the state of the triggers in `LuiIndexInfo`:** they cost a statement per index, and the list is meant for every probe.
  `Count(name)` and `Check(name)` are asked by name when they are wanted; #266 adds the one state field that is cheap (`NeedsInitialization`).

## Consequences

- A tool that opens a file it does not know can list, count, check and maintain every index of it. A health endpoint calls `Check(name)` and
  reads no payload.
- `LuiIndexInfo` is public API: a new member of it is a break for a caller that constructs one only if it is a `required` property; a
  caller reads it. The guard `PublicApiShapeTests` lists the members without a filter.
- Each operation by name builds a runtime — the prepared SQL of the index — for the call. The cost is measured with the rest of the
  release (#275); no benchmark is part of this change.
- #266 adds its sections to this record: `catalog.Initialize()`, the registration of the outbox and the host pattern.
