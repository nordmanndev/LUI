# 0035 — The index knows no types: the serialiser belongs to the catalog, the type stands where the payload is used

- **Status:** Accepted
- **Date:** 2026-11-06
- **Builds on:** [0033](./0033-the-read-path-opens-an-index-without-writing.md) (the catalog is the entry point, the file
  decides what may be written), [0026](./0026-the-public-api-from-0-1-0-on-grows-only-additively.md) (before `0.1.0` a
  name may change), [0013](./0013-the-api-shape-before-the-first-release.md) (the shape of the API),
  [0015](./0015-the-linked-index-is-loosely-coupled.md) (the linked index)
- **Followed by:** issue #59 (a typed view over the untyped index, only when users ask for it)
- **Affects:**
  - `src/Nordmann.LUI/ILuiIndex.cs`, `ILuiEmbeddedIndex.cs`, `ILuiLinkedIndex.cs`, `LuiEntry.cs`, `LuiHit.cs`,
    `LuiResult.cs`, `LuiQuery.cs`, `LuiEmbeddedIndexBuilder.cs`, `LuiLinkedIndexBuilder.cs`, `LuiIndexKindBuilder.cs`,
    `LuiCatalog.cs`, `LuiCatalogOptions.cs` (no type parameter, the serialiser of the catalog)
  - `src/Nordmann.LUI/LuiKey.cs`, `LuiKeyKind.cs`, `ILuiPayloadSerializer.cs`, `LuiPayloadSerializer.cs`,
    `LuiPayloadException.cs` (new); `LuiNoPayload.cs`, `LuiJsonPayloadSerializer.cs`,
    `LuiEmbeddedIndexBuilderExtensions.cs` (gone)
  - `src/Nordmann.LUI/Schema/IndexDefinition.cs`, `Schema/KeyCodec.cs`, `Querying/*Payload*.cs`, `Querying/HitReader.cs`,
    `Querying/QueryRunner.cs`, `Indexes/Embedded*.cs`, `Indexes/LinkedIndex.cs`
  - spec §7.1, §7.3, §8.3, §12.1–§12.9; README, CHANGELOG, `docs/testing.md`, `tools/package-smoke-test.sh`, the sample
- **Issue:** #58 (follows #56)

## Context

After ADR-0033 the catalog was the one way into an index, and every way in named the types of the application:

```csharp
var tags = catalog.Index<long, TagChip>("tags", new LuiJsonPayloadSerializer<TagChip>(ctx.TagChip));
//                       ^^^^  ^^^^^^^                                  ^^^^^^^  ^^^^^^^^^^
//                       key   payload            and the payload once more, twice
```

The owner's verdict: working with an index means knowing its types, at every place it is opened — and that is the wrong
place. The types also promised a safety the index does not have. The file stores **every key as text**
(`key TEXT NOT NULL UNIQUE`) and **every payload as bytes** (`payload BLOB`), whatever the type parameters say; nothing
in the index looks at the payload, and the type check of ADR-0033 compared the CLR name of a class — which a rename or
a move into another namespace breaks, while a real change of the payload passes. `LuiNoPayload` existed only so that the
signatures fit.

## Decision

**The index knows no types. The serialiser belongs to the catalog, and the type of a payload stands where the payload is
used.**

```csharp
using var catalog = LuiCatalog.Open(connectionString, new LuiCatalogOptions
{
    PayloadSerializer = LuiPayloadSerializer.Json(AppJsonContext.Default),   // one per catalog
});

catalog.Create("tags", index => index.SingleWord());                          // no type
var tags = catalog.Index("tags");                                             // no type

tags.Upsert(new LuiEntry(1, "vegan") { Weight = 120, Payload = new TagChip("#3a3", "System") });
var chip = tags.Search("veg").Hits[0].GetPayload<TagChip>();                  // the type, where it is used
```

1. **The serialiser is the catalog's**: `LuiCatalogOptions.PayloadSerializer`, one `ILuiPayloadSerializer` without a
   type parameter — `byte[] Serialize(object payload)` with the type the payload has at run time,
   `T Deserialize<T>(ReadOnlySpan<byte> bytes)` with the type the caller asks for. `LuiPayloadSerializer.Json(context)`
   takes the source-generated `JsonSerializerContext` of the application (AOT- and trim-safe); a type that is not in the
   context is refused on the write and on the read, and the message names it. Without a serialiser the indexes store no
   payload, and an entry with one is refused.
2. **No public type of an index has a type parameter**: `ILuiIndex`, `ILuiEmbeddedIndex`, `ILuiLinkedIndex`, `LuiEntry`,
   `LuiHit`, `LuiResult`, `LuiQuery` and the builders. One implementation per capability, no second facade.
3. **`LuiKey`**: a value type that is a number or a text. The conversions *into* it (`long`, `string`) are implicit — they
   lose nothing and never throw, so `index.Remove(42)` and `index.Remove("01J…")` read as before; the conversions *out*
   are explicit (`(long)hit.Key`) and throw for the other kind. `IEquatable<LuiKey>`, ordinal for texts; `1` and `"1"`
   are different keys. The definition keeps the **kind** of the keys (`WithKey(LuiKeyKind.Text)`; numbers by default) —
   not a type: it decides how a stored key is read. A key of the other kind fails at once on the write.
4. **The payload is read where it is used**: `hit.GetPayload<T>()`, `hit.TryGetPayload<T>(out T)`, `hit.HasPayload`. The
   query hands out the stored bytes and deserialises nothing; the hit deserialises on every call. What cannot be read
   as `T` is a `LuiPayloadException` with the index, the key and the type — whatever the serialiser throws, LUI's or one
   of the application.
   **`LuiPayloadSerializer.Json` reads strictly** (review of PR 60, I1): with the file naming no type, the bytes are the
   only witness of what was written, and JSON is tolerant — a chip read as a recipe came back as
   `Recipe { Title = "", Minutes = 0 }`, wrong data that looks like right data. The serialiser therefore reads with the
   settings of the context plus `UnmappedMemberHandling.Disallow` (a member in the bytes the type does not have) and
   `RespectRequiredConstructorParameters` (a parameter of the constructor the bytes do not have): both are a
   `JsonException`, and the hit makes it a `LuiPayloadException`. A type that has every member of the bytes and optional
   ones besides is read — that is a compatible change of the payload, not a mistake. No name is stored for it.
5. **Opening**: `catalog.Index(name)` for an embedded index, `catalog.LinkedIndex(name)` and
   `catalog.LinkedIndex(name, map)` for a linked one — the name says the source, because the return type has to be known
   when the code is compiled and the two sources can do different things. The type of the map is inferred from the
   lambda; nobody writes it.
6. **No type name in the file**: `IndexDefinition.PayloadType` goes, and so does the flag `Payload` — every entry of an
   embedded index may carry a payload, the column has always been there. A definition that still carries the two fields
   is read and they are ignored, so the files of #56 open unchanged. Changing the payload type is the application's
   responsibility; at the latest the serialiser says so when it cannot read the bytes.
7. **`Linked` stays as it is** — the payload is built from columns of the table by the map of the application — and is
   documented precisely: LUI keeps **no copy of the row** — the normalised text in the search index, the weight and the
   priority packed into the order of the candidates; the text and the payload of a hit are read from the table when the
   hit is read. A changed payload column is visible at once; a new text becomes searchable, and a new weight or priority
   counts, after `Refresh`, `Sync` or the triggers.

`null` is "no payload"; any other value is one — also `0` (before, a value-type payload that was its default was not
stored; that rule needed the type).

## Alternatives

| | For | Against |
|---|---|---|
| **A — keep the types**, `ILuiEmbeddedIndex<TKey, TPayload>` (EF Core's `DbSet<T>`, `VectorStoreCollection<TKey, TRecord>`) | the compiler checks key and payload at every call | ceremony at every place an index is opened; `LuiNoPayload` as a placeholder; the safety is claimed, not had — the file is never checked against the payload type, only a CLR name is compared; generic interfaces are hard to grow additively after `0.1.0` |
| **B — a document base class**, `MyDoc : LuiDocument<long, TagChip>` with a static serialiser (the first plan of #56) | one type for key, payload and hit | does not compile as planned: a `static virtual` member is allowed on an interface only (`CS0112` on a record), C# does not infer `TKey` from `TDoc` (`CS0305`) — it needs a self-referencing type parameter, a factory to build the document, and extension methods for the typed key; much machinery for little |
| **C — the index knows no types** (this decision) | the types stand where the payload is used; one implementation; interfaces without type parameters, easy to mock and to grow additively; a payload that is not read is not deserialised | the kind of the key and the type of the payload are checked at run time — the key at once on the write, the payload when it is read |
| **D — C plus a typed view**, `index.As<TagChip>()` | who wants the compiler's check gets it | a second way into the data on the surface |

The precedents of the platform are C, not A, where the payload is opaque: Azure Cosmos' untyped `Container` with
`ReadItemAsync<T>` per call and its `PartitionKey` value type over string and number, Azure AI Search's untyped
`SearchClient` with `Search<T>`, `Autocomplete` and `Suggest<T>`, `HttpContent.ReadFromJsonAsync<T>`,
`BinaryData.ToObjectFromJson<T>`, `IDistributedCache` with bytes only. A is right where the type *is* the schema — EF Core
maps columns to properties; LUI's payload is a blob the index never looks at.

**Why C and not A — the argument of versioning.** From `v0.1.0` the API only grows (ADR-0026). Starting with C, D can be
added at any time, additively, when real users ask for it (#59). Starting with A, the type parameters can never be taken
out again. C is the decision that can still be corrected; A is not.

## Consequences

- **What the application writes**: no type to open an index, no serialiser per index, no placeholder. The type of a
  payload appears where it is read (`GetPayload<T>()`) — and, for a linked index, in the map it brings.
- **Measured** (`GC.GetAllocatedBytesForCurrentThread()`, the median of 31 rounds of eight inputs, 200,000 entries of the
  German word list, single word, 20 hits; the same probe on `main` at `73ef344` and on this branch, two runs each, the
  same figures):

  | per query | before | after | difference |
  |---|---:|---:|---:|
  | no payload | 33.66 KB | 34.27 KB | +0.61 KB (+1.8 %) |
  | a payload, not read | 38.43 KB | **35.52 KB** | −2.91 KB (−7.6 %) |
  | a payload, all 20 hits read | 38.90 KB | 39.21 KB | +0.31 KB (+0.8 %) |

  The hit is a larger struct now — the key holds a number and a reference, and the hit the bytes and their source
  (about +24 B per hit, 20 hits) —, and the payload column is selected for every embedded index. A query whose payloads
  are not read is cheaper, because nothing is deserialised. All three are far below the target of §16 (p50 ≤ 60 KB,
  ADR-0032).
- **A payload is deserialised on every `GetPayload<T>()`** — the hit does not cache it, which keeps the hit a small
  value. The application keeps the result when it uses it twice (documented on the method).
- **A type mistake surfaces later, but loudly**: a wrong key kind on the write (at once, `ArgumentException`), a payload
  read as a type of another shape when it is read (`LuiPayloadException`, by the strict reading). What stays silent is a
  type that has all the members of the bytes — by construction a compatible one. The price is accepted; #59 is the way
  back to the compiler for the places that want it.
- **The strictness has a cost for the application**: a member that is removed from a payload type, or a constructor
  parameter that is added without a default, makes the payloads written before unreadable — loudly. That is the change of
  the payload that ADR-0035 leaves to the application: it writes the entries again (`Sync`) or keeps the type compatible.
- **A key type a definition names that this version does not know** (neither `long` nor `string`) is a
  `LuiDefinitionMismatchException` when the index is opened (review of PR 60, M3), not a `FormatException` at the first
  hit.
- **A hit is a value** (review of PR 60, M4): its equality compares the stored payload by its content — the bytes, or
  what the map of a linked index built — so two hits of the same entry are equal, as they were when the hit held the
  deserialised payload. Where the payload came from is no part of it.
- **One serialiser per catalog.** Two indexes with two formats in one file are not possible. If that is ever needed, a
  serialiser per index is an additive option of the builder.
- **ADR-0033's "the untyped facade is gone" holds**: the facade was a *second* way into the data beside the typed one.
  There is still exactly one way — and it names no type.
- **Breaking, before `0.1.0`**: allowed by ADR-0026; the migration list is in the CHANGELOG.

**Verified by:** `tests/Nordmann.LUI.Unit.Tests/LuiKeyTests.cs`, `tests/Nordmann.LUI.Unit.Tests/LuiPayloadSerializerTests.cs`,
`tests/Nordmann.LUI.Integration.Tests/ReadPathTests.cs` (the kind of the key from the file, the key of the other kind,
the serialiser of the catalog that opens the file, the type that cannot be read, a payload read as a type of another
shape, whatever a serialiser of the application throws, the definition of an older version and of an unknown key type,
the source of the index), `tests/Nordmann.LUI.Integration.Tests/EmbeddedIndexTests.cs` (the payload is no part of the
definition, the type the serialiser does not know, the catalog without a serialiser, the payload read when it is asked
for and not before, two hits of the same entry are equal), `tests/Nordmann.LUI.Architecture.Tests/Public_API_Shape.cs` (`The_index_knows_no_types`, the shape
of the catalog, the retired names), `tests/Nordmann.LUI.Unit.Tests/KeyCodecTests.cs` (a key of the other kind refused before it reaches the file), the
README run by `tools/package-smoke-test.sh`, the mutations `the-hit-gets-a-payload-property` and
`a-key-of-the-other-kind-reaches-the-file` and `the-json-payload-is-read-tolerantly` of `tools/mutation-check.sh`, and the probe above.
