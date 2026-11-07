# 0044 — The typed view over the untyped index

- **Status:** Accepted
- **Date:** 2026-11-07
- **Builds on:** [0026](./0026-the-public-api-from-0-1-0-on-grows-only-additively.md) (the API grows only additively),
  [0033](./0033-the-read-path-opens-an-index-without-writing.md) (the read path opens an index without writing),
  [0035](./0035-the-index-knows-no-types.md) (the index knows no types), [0038](./0038-the-public-api-has-a-page-of-its-own.md) (the public API has a page of its own)
- **Affects:**
  - `src/Nordmann.LUI/LuiTypedIndex.cs` (the view), `src/Nordmann.LUI/LuiTypedResult.cs` (the answer),
    `src/Nordmann.LUI/LuiTypedHit.cs` (the hit)
  - `src/Nordmann.LUI/LuiIndexExtensions.cs` (the registration `As<T>()`)
  - `docs/api.md`, the CHANGELOG, the README, the tutorial
  - `tests/Nordmann.LUI.Architecture.Tests/Public_API_Shape.cs` (the list of the types with named properties)
  - `tests/Nordmann.LUI.Architecture.Tests/Type_Names.cs` (the name of a type without the arity)
  - `tests/Nordmann.LUI.Integration.Tests/TypedIndexTests.cs` (the two cases)
- **Issue:** #59

## Context

ADR-0035 took the type out of the index: the payload is stored as bytes, the serialiser belongs to the catalog, and the
type stands where the payload is used — `hit.GetPayload<TagChip>()`. That keeps the file free of type names and lets an
application change the type of its payload without a migration.

The price is at the place where the payload is read: the type is written out at every call, and a typo in it is a
runtime error, not a compile error.

```csharp
var chip = catalog.Index("tags").Search("veg").Hits[0].GetPayload<TagChip>();
```

## Decision

**`index.As<T>()` gives a typed view of the same index.** It is a view, not a second implementation.

- The **name** is `As<T>()`. `Typed<T>()` says what the view is, `WithPayload<T>()` says what it carries — `As<T>()`
  says what it does with the index, and that is what the caller asks for.
- The **view** is `LuiTypedIndex<TPayload>`; it wraps the one index and forwards to it. It opens nothing, writes
  nothing, and holds no index of its own.
- The **answer** is `LuiTypedResult<TPayload>` and the **hit** is `LuiTypedHit<TPayload>` — the typed world has three
  types, the untyped world has three (`ILuiIndex`, `LuiResult`, `LuiHit`), and the view mirrors them one to one.
- The **key stays `LuiKey`**. A second typed key view would be a second concept for the same thing (ADR-0013), and the
  key of an entry is read rarely compared with its payload.
- The **payload is not cached**. `LuiHit.GetPayload<T>()` deserialises on every call, and the typed hit keeps that
  promise: `Payload` reads the payload when it is asked, exactly like `GetPayload<T>()` does. A cache per hit is memory
  and an invalidation — and the caller who needs the payload twice keeps it, as the page of the API says.
- **The index still knows no type.** The type parameter belongs to the *view*, which is the application’s; the index,
  the catalog and the file stay free of it (ADR-0035). The file keeps no type name.
- The **view offers `Search`** — the query in one call. The fluent query stays untyped: it configures the query, and
  the answer is read with `Search`.

## Consequences

- The application that wants the payload type checked by the compiler writes `index.As<TagChip>()` once and reads
  `hit.Payload` — with no type parameter at the call, and with the type checked.
- **The view is additive** (ADR-0026): `GetPayload<T>()` stays and stays the one way for the application that does not
  want a view. Nothing changes for the application that does not ask for it.
- The typed hit is a **struct that wraps the hit of the index**: it copies the six fields of the hit and reads the
  payload through it. There is no second copy of the data in the file and no cache in the memory.
- The page of the API (`docs/api.md`) carries the three new types (ADR-0038) and the guard of the types with named
  properties lists the two new records.
- **These are the first generic types of the public surface.** The runtime appends the arity to the name of a generic
  type (`LuiTypedHit`1`), and the guards that compare the page with the assemblies must ask the same question on both
  sides: the name without the arity. The page writes `LuiTypedHit` — a document names a type, not a closed generic.

## Alternatives

| The alternative | Why not |
|---|---|
| `GetPayload<T>()` stays the only way | That is the decision of #59 against: the type is written at every read and a typo in it is a runtime error. The untyped way stays — the typed way is added. |
| `Typed<T>()` or `WithPayload<T>()` as the name | `Typed<T>()` says what the view is, `WithPayload<T>()` what it carries. The caller asks for a view of an index — `As<T>()` says that. |
| A typed key view (`As<long, TagChip>()`) | A second concept for the same key (ADR-0013). The payload is read at every hit, the key rarely. |
| The view caches the payload per hit | Memory and an invalidation, for a value the caller may read once. `GetPayload<T>()` deserialises on every call — and the typed hit keeps that promise. |
| The index itself grows `As<T>()` | Then the index knows a type (ADR-0035). The view is the application’s, and the extension method keeps it out of the index. |
| The typed view offers a typed fluent query | The fluent query configures the query; the answer is read with `Search`. A second query builder would be a second implementation of the same thing. |
