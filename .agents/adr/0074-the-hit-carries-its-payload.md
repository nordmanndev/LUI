# 0074 — The hit carries its payload: a query loads the payloads of its hits, and an unreadable one fails the query

- **Status:** Accepted (owner, 2026-11-20)
- **Date:** 2026-11-22
- **Supersedes:** [0044](./0044-the-typed-view-over-the-untyped-index.md) in two points — *the payload is not cached* and *it is read by a method* — for the hits of a handle with a payload type; and [0035](./0035-the-index-knows-no-types.md) in one — *a payload that is never read costs nothing*
- **Builds on:** [0073](./0073-the-index-definition-and-the-typed-handles.md) (the payload variants of the handles and of the hit),
  [0072](./0072-the-serializer-is-bound-to-the-payload-type-through-a-registry.md) (the serializer is bound to the payload type; the fingerprint),
  [0075](./0075-the-catalog-holds-the-definitions-and-the-runtime-of-an-index.md) (the guard inside the snapshot of an operation; the runtime holds the serializer),
  [0035](./0035-the-index-knows-no-types.md) (the index stores bytes; the strict JSON reading),
  [0032](./0032-the-allocation-of-a-query.md) (the allocation budget of a query),
  [0071](./0071-the-freeze-comes-with-1-0-0.md) (a break before 1.0.0 needs a reason, a migration note and a record)
- **Affects:**
  - `src/Nordmann.LUI/LuiHitOfKeyAndPayload.cs`, `src/Nordmann.LUI/RawHit.cs` — the stored payload, set when the result is built
  - `src/Nordmann.LUI/Querying/PayloadSource.cs`, `src/Nordmann.LUI/Querying/ColumnPayload.cs` — a payload that is missing, a map that throws or returns null
  - `src/Nordmann.LUI/Indexes/IndexRuntimes.cs`, `src/Nordmann.LUI/Indexes/EmbeddedIndex.cs` — a handle without a payload type selects no payload column
  - `src/Nordmann.LUI/LuiEmbeddedIndexOfKeyAndPayload.cs`, `src/Nordmann.LUI/Indexes/EmbeddedCheck.cs`, `src/Nordmann.LUI/Schema/EmbeddedSource.cs` — `CheckPayloads`
  - `src/Nordmann.LUI/LuiPayloadCheck.cs`, `src/Nordmann.LUI/LuiUnreadablePayload.cs` — the report
  - `src/Nordmann.LUI/Schema/IndexDescription.cs`, `src/Nordmann.LUI/LuiLinkedIndexBuilderOfPayload.cs`, `src/Nordmann.LUI/LuiCatalog.cs` — the type a map builds, for the message
  - `docs/api.md`, `docs/testing.md`, `CHANGELOG.md`, `.agents/adr/README.md`, `.agents/adr/0035-the-index-knows-no-types.md`, `.agents/adr/0044-the-typed-view-over-the-untyped-index.md`
  - `tests/Nordmann.LUI.Integration.Tests/Querying/HitPayloadTests.cs`, `tests/Nordmann.LUI.Integration.Tests/Maintenance/CheckPayloadsTests.cs`, `tests/Nordmann.LUI.Architecture.Tests/PublicApiShapeTests.cs`, `tools/mutation-check.sh` — the rules and the guards of the shape
- **Issue:** [#262](https://github.com/nordmanndev/LUI/issues/262), part of the epic of 0.4.0 ([#258](https://github.com/nordmanndev/LUI/issues/258))

## Context

ADR-0044 stated two things about the payload of a typed hit: *the payload is not cached* and *it is read by a method*. A property
that deserializes on every access and can throw breaks what a caller expects of a property, which is why `LuiTypedHit<TPayload>.Payload`
became `GetPayload()` in the review of PR 69. ADR-0035 made the cost of that visible and called it a virtue: a payload that is never read
costs nothing.

With a declared payload type (0073) the hit knows the type, and the application should not have to ask for the payload of each hit one by
one — nor to decide, hit by hit, whether the read may throw. The owner decided on 2026-11-20: a hit with a payload exposes it; the query
loads the payloads of its hits **together with the query**; `hit.Payload` is then a plain value. There is no lazy mode, and there is no
untyped payload.

## Decision

1. **`LuiHit<TKey, TPayload>.Payload` is a plain value**, set when the result is built and never changed (`init`, no setter). Reading it does no
   I/O, calls no serializer and no map, throws nothing, caches nothing, and works after the catalog has been disposed — the result is a
   self-contained value.
   - **Embedded:** the payload bytes are selected with the hit rows, **in the same statement and the same snapshot** as the keys of the hits
     (no second read connection, no second read), and deserialized with the serializer of the payload type **before `Search` or `Execute`
     returns**: one call per hit.
   - **Linked:** the map of the definition runs once per hit, in the loop that reads the hit rows, before `Search` or `Execute` returns.
2. **A hit of a handle without a payload type has no payload.** `LuiHit<TKey>` has no `Payload` and no `HasPayload`; the runtime of such a
   handle **selects no payload column** — the bytes an old call may have stored in the index are never copied. The untyped `LuiHit` keeps
   `GetPayload<T>()` until #268 removes it.
3. **The guarantee of 0035 that a payload that is never read costs nothing is withdrawn.** A query on an index with a payload reads and
   deserializes every payload of its result; a query on an index without one reads none. The cost is bounded by `MaxResults` (20 by default,
   at most 1,000): at most that many payloads per query.
4. **An unreadable payload fails the query — fail fast.** A serializer that throws, a stored payload that is missing, a map that throws and a
   map that returns `null` each fail `Search` / `Execute` with a `LuiPayloadException` that names the **index, the key and the type**
   (the exception of the serializer or the map is the inner one). Accessing a hit never throws. This is a deliberate trade-off: **one broken
   payload fails the whole query** instead of returning a partial result. A partial result would hide the broken entry behind a list that
   looks complete; an application that wants to skip it has nothing to skip it with — the list of the broken ones is `CheckPayloads()`.
5. **"Unreadable" is the serializer's verdict, not LUI's.** LUI fails the query when the registered serializer throws. The built-in
   `LuiPayloadSerializer.Json` is strict (`UnmappedMemberHandling.Disallow`, `RespectRequiredConstructorParameters`, 0035): a payload of an old
   shape throws. A tolerant serializer registered with `Use<T>(…)` may read the old shape with default values — then nothing fails. Every
   statement about old payloads is about `LuiPayloadSerializer.Json`.
6. **`CheckPayloads(ct)` finds the broken payloads.** `LuiEmbeddedIndex<TKey, TPayload>.CheckPayloads(CancellationToken)` returns a
   `LuiPayloadCheck<TKey>` — `Unreadable` (a list of `LuiUnreadablePayload<TKey>`: `Key`, `Message`) and `IsHealthy` — as **a full read of the
   index**: every stored payload, once, in one snapshot on one read connection, with the guard of the handle inside it (a dropped or recreated
   index is a `LuiIndexNotFoundException` or `LuiDefinitionMismatchException` as for any operation), cancellable between the entries, allowed
   on a read-only catalog. It uses the same source and the same rule as a query, so an entry is on the list exactly when a query that met it
   would fail. It is for maintenance, not for every probe. A linked index has none: its payload is not stored.
   **`Check()` stays the structural check** (0016): a report without keys that reads **no payload** and calls **no map** — cheap enough for a
   health endpoint. The two are apart on purpose.
7. **Changing a payload type** (with `LuiPayloadSerializer.Json`): removing or renaming a field makes the stored payloads of the old shape
   unreadable, and **every query whose result holds such an entry fails — the search of that index is effectively down until the entries are
   rewritten.** Adding a field with a default value keeps the old payloads readable. The documented ways are
   (a) **a new index** (`recipes_v2`, a new definition) filled from the source, the application switched over, the old index dropped —
   without a window, the recipe of 0073 for a changed definition; and (b) **in place**: deploy the new type and rewrite every entry with
   `Upsert`, `UpsertMany` or `Sync`, with `CheckPayloads()` listing the ones still missing. The window of (b) is
   *entries ÷ the rate of `UpsertMany`*: 26,052 entries per second on the reference machine (`docs/benchmarks.md`, "The batch"), about 40
   seconds per million entries, about one and a half minutes when a whole large index is loaded (87 s per million in "The build"). The
   page says so, with the rule.
8. **Upgrading payloads belongs to the migration topic** of a later milestone (#258): tolerant reading by LUI, skipping unreadable payloads,
   upgrade functions, versioned payloads. In 0.4.0 an unreadable payload fails the query — that is accepted.
9. **The budget of a query with a payload** is part of the performance budget of the release: 20 hits, a record of three to five fields, about
   200 bytes of JSON, on the reference corpus — the allocation within the budget of 0032 (p50 ≤ 60 KB, p95 ≤ 100 KB) and the median at most
   15 % over the same query without payload. It is measured once, at the end of the release (#275, the owner, 2026-11-21); the benchmark
   is written there. The test of the repository holds the allocation of such a query below 60 KB on a small corpus.

## Alternatives

| | For | Against |
|---|---|---|
| **A — keep `GetPayload()`** (0044) | nothing to change; a payload that is never read costs nothing | one read per call, a throw from a method that looks like a read of a field; the application decides hit by hit |
| **B — a lazy property that caches** | the cost only for the payloads that are read | I/O and an exception behind a property, a connection or a cache that the hit has to hold, a hit that does not survive the catalog |
| **C — load with the query, fail the query** (this decision) | a hit is a plain value; the cost is one bounded read; the failure is one place | a broken payload takes the whole query down until it is rewritten |
| **D — load with the query, skip the broken hits** | the search stays up | a result that looks complete and is not; the owner rejected it — repair is `CheckPayloads()` and a rewrite |

## Consequences

- **Breaking, before 1.0.0** (0071): `LuiTypedHit<T>.GetPayload()` / `TryGetPayload` and `LuiHit.GetPayload<T>()` stay until #268 and are the
  old calls; the typed handles get the property. The CHANGELOG has the migration note.
- **The hot path of a query without payload does not change** — it selects fewer columns than before for a handle without a payload type, and
  not one more for any other; the statement and the loop of the hits are shared. A query with a payload costs the bytes of each hit and one
  deserialization per hit: the benchmark that measures it is written for #275.
- **A broken payload takes a search down** until its entry is rewritten; the window is documented, and `CheckPayloads()` is how an operator
  finds the entries. A health endpoint calls `Check()`, which reads no payload; a maintenance job calls `CheckPayloads()`.
- **`CheckPayloads()` lists every unreadable entry**: on an index whose payload type was changed it holds one object per entry until the
  entries are rewritten — a list of the size of the index, held for the length of the call.
- **A map that throws is wrapped on the typed handles only.** The exception that the map threw is the inner one of the
  `LuiPayloadException`. The old calls (`CreateLinked`, `LinkedIndex(name, map)`) name no payload type and keep the exception of the map as
  it was, until #268 removes them.
