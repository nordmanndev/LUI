# 0072 — The serializer is bound to the payload type through a registry, and an embedded index stores the name and the format of its payload

- **Status:** Accepted (owner, 2026-11-20)
- **Date:** 2026-11-22
- **Supersedes:** [0035](./0035-the-index-knows-no-types.md) in two points: *one serialiser per catalog* (§1), and *no type name in the file* (§6) — for the name of the payload type and the format of an embedded index
- **Builds on:** [0073](./0073-the-index-definition-and-the-typed-handles.md) (the definition and the typed handles; the payload variants; `LuiConfigurationException`; the compared fields),
  [0075](./0075-the-catalog-holds-the-definitions-and-the-runtime-of-an-index.md) (the runtime lives in the catalog with the definition; the handle is thin; the guard of the schema counter),
  [0035](./0035-the-index-knows-no-types.md) (the index stores bytes; the strict JSON reading),
  [0033](./0033-the-read-path-opens-an-index-without-writing.md) (opening writes nothing),
  [0071](./0071-the-freeze-comes-with-1-0-0.md) (a break before 1.0.0 needs a reason, a migration note and a record)
- **Affects:**
  - `src/Nordmann.LUI/LuiPayloadRegistry.cs`, `src/Nordmann.LUI/ILuiPayloadSerializerOfPayload.cs`, `src/Nordmann.LUI/LuiPayloadNameAttribute.cs` — the registry, the generic serializer, the attribute
  - `src/Nordmann.LUI/LuiPayloadSerializer.cs`, `src/Nordmann.LUI/LuiCatalogOptions.cs` — `Json<TPayload>(context)` with the format `json-strict-v1`; `Payloads`
  - `src/Nordmann.LUI/Schema/PayloadTypeName.cs`, `src/Nordmann.LUI/Schema/PayloadFingerprint.cs`, `src/Nordmann.LUI/Schema/IndexDefinition.cs`, `src/Nordmann.LUI/Schema/IndexDescription.cs` — the stable name, the two stored fields
  - `src/Nordmann.LUI/Querying/FrozenPayloadRegistry.cs`, `PayloadCodec.cs`, `PayloadSerialization.cs`, `LegacyPayloadSerialization.cs`, `RegistryPayloadSerialization.cs`, `StoredFormatPayloadSerialization.cs`, `PayloadSource.cs`, `SerializedPayload.cs` — the frozen copy, the codec of a type, the three ways to serialize
  - `src/Nordmann.LUI/LuiCatalog.cs`, `src/Nordmann.LUI/LuiIndexDefinitionBase.cs`, `src/Nordmann.LUI/LuiIndexDefinitionOfKeyAndPayload.cs`, `src/Nordmann.LUI/LuiEmbeddedIndexOfKeyAndPayload.cs` — the lookup at `Create` and `Index`, once per runtime
  - `src/Nordmann.LUI/Indexes/IndexStore.cs`, `src/Nordmann.LUI/LuiDefinitionMismatchException.cs`, `src/Nordmann.LUI/Indexes/IndexRuntimes.cs`, `src/Nordmann.LUI/Indexes/EmbeddedIndex.cs` — the comparison and the message, the codec in the runtime
  - `docs/api.md`, `docs/testing.md`, `CHANGELOG.md`, `.agents/adr/README.md`, `.agents/adr/0035-the-index-knows-no-types.md`
  - `tests/Nordmann.LUI.Architecture.Tests/PublicApiShapeTests.cs`, `tools/mutation-check.sh` — the shape of the registry and the guard of the retired names
- **Issue:** [#260](https://github.com/nordmanndev/LUI/issues/260), part of the epic of 0.4.0 ([#258](https://github.com/nordmanndev/LUI/issues/258))

## Context

ADR-0035 gave a catalog **one** `ILuiPayloadSerializer` for the whole file and stored no type name. Two things follow that
a 0.4.0 application cannot live with:

- **Two indexes with two formats in one file are impossible.** The stored bytes carry no format marker, so a catalog that
  holds the JSON payloads of the recipes and the MessagePack payloads of an attachment index has no way to say which
  index is read with which serializer.
- **A payload is read silently as the wrong thing.** ADR-0035 answered "what if the type changes" with the application's
  responsibility and the strict JSON reading. The reading catches a chip read as a recipe; it does not catch the same type
  written by another format (the bytes are garbage, or — worse — another format that happens to parse), and it cannot catch
  at all with a serializer that is not strict.

ADR-0073 made the type of a payload a declaration: `LuiIndexDefinition<TKey, TPayload>`. The owner's decisions of
2026-11-20 follow from that: the serializer is bound to the payload type, an embedded index stores a fingerprint of its
payload, there is no untyped payload, and a configuration error is not a payload error.

## Decision

1. **A registry maps payload types to serializers** — `LuiPayloadRegistry`, set in `LuiCatalogOptions.Payloads`:
   - `Use(JsonSerializerContext)` registers a context; every payload type it lists is read and written as strict JSON. A
     context cannot list its types, so a type is looked up in the contexts when an index needs it (`TryGetTypeInfo`), as before.
   - `Use<TPayload>(ILuiPayloadSerializer<TPayload>)` registers a serializer for exactly one type: MessagePack, protocol buffers,
     reflection-based `JsonSerializerOptions`, a format of the application.
   - **There is no catch-all** — no way to register one serializer for every type.
2. **`ILuiPayloadSerializer<TPayload>`** is the serializer of one type: `string Format`, `byte[] Serialize(TPayload)`,
   `TPayload Deserialize(ReadOnlySpan<byte>)`. The format is a name — `json-strict-v1`, `msgpack-v1` — that is stored and compared;
   **a serializer that changes its wire format must change its `Format`**, LUI compares the name and not the bytes. It is read once,
   when the serializer is registered. `LuiPayloadSerializer.Json<TPayload>(context)` is the strict JSON serializer of one type, the
   one `Use(context)` gives (format `json-strict-v1`); the old non-generic `ILuiPayloadSerializer` and `Json(context)` stay until #268.
3. **No silent precedence.** An explicit `Use<TPayload>` wins over the contexts. Registering the same type twice with it throws a
   `LuiConfigurationException` at the registration. Two contexts that both know a type that an index needs throw a
   `LuiConfigurationException` when the index is created or opened, naming both — the order of the `Use` calls never decides. The same
   context registered twice is one registration.
4. **The registry is frozen when the catalog is opened.** `Open`, `OpenOrCreate` and `OpenReadOnly` copy it (before any file is touched);
   a call on the registry afterwards changes nothing for that catalog, and a catalog opened later sees it. Registration is
   thread-safe. Setting `Payloads` **and** `PayloadSerializer` is a `LuiConfigurationException` at open — a catalog has one way to
   serialize; `PayloadSerializer` stays until #268 for the old calls.
5. **The lookup happens once, and fails fast.** `catalog.Create(definition)` and `catalog.Index(definition)` of an embedded definition with
   a payload look up `TPayload` before anything is written or opened: an unregistered type (no registry, a registry that does not know
   the type, or two contexts that do) is a `LuiConfigurationException` that names the definition and the type and says how to register
   it, and **no index is created for it**. The answer — or the refusal, kept as a record, with a new exception on every use — is kept
   in the registry copy of the catalog per type, and the serializer lives in the runtime of the index (0075): a write or a read never
   looks it up again. `LuiPayloadException` stays for the payloads that cannot be read.
6. **A fingerprint of the payload is part of an embedded definition.** `lui_meta` stores the **name of the payload type**
   (`payloadTypeName`) and the **format** of its serializer (`payloadFormat`); both are compared fields (0073 §6) and part of LUI format 2.
   A linked index stores neither: its payload is built from the columns of its table by the map of its definition, and needs no registry.
   Only the two names are stored — not the settings of the serializer. The name of an older definition's `payloadType` field (ADR-0035
   §6: read and ignored) is not reused, so such a row opens as before.
7. **The name of a type is stable.** It is the value of `[LuiPayloadName("…")]` if the type carries the attribute; otherwise LUI computes it
   without assembly, version or key: a non-generic type is its namespace-qualified name, nested types `Outer+Inner`; a generic type its
   name without the arity and with the names of its arguments — `MyApp.Envelope<MyApp.Recipes.RecipePayload>`,
   `System.Collections.Generic.Dictionary<System.String,System.Int32>`; an array the name of its element type with `[]` (`[,]` for two
   dimensions). An attribute on a generic type keeps the arguments after its name (`wrapped<…>`), so two closed types are not one name.
   **Put the attribute on every payload type**: without it, renaming the class or its namespace is a changed definition, and for an
   embedded index that means drop and fill again. (Renaming a stored name in place belongs to the migration topic of #258.) **Two
   different types with the same name look the same to LUI** — the duplicate is not detected across types.
8. **A mismatch names what differs** and is a `LuiDefinitionMismatchException`: another type — *stored 'recipe', declared
   'MyApp.Other'*, with the `[LuiPayloadName]` hint when the names differ only in the namespace —; the same type in another format —
   *the payloads of 'recipes' were written in format 'json-strict-v1', the registered serializer writes 'msgpack-v1'*. The fields are
   `payloadTypeName` and `payloadFormat`.
9. **One index, one kind of definition.** An index stored with a payload type opens only with a `LuiIndexDefinition<TKey, TPayload>`, an
   index stored without one only with a `LuiIndexDefinition<TKey>` — both ways are a mismatch that says which to use. A handle of an index
   that another process dropped and created again in another format is stale, like any other (0075): the guard of its operation finds the
   changed definition and throws a mismatch, so it never writes through the old serializer into the new index.
10. **Until #268 removes the old calls.** The read path by name (`catalog.Index(name)` and `hit.GetPayload<T>()`) resolves `T` through the
    registry when `Payloads` is set — a type that the registry does not know is a `LuiConfigurationException`, not a payload error — and
    through `PayloadSerializer` otherwise. With a registry there is **no untyped payload**: an entry of an index without a declared payload
    type cannot carry one (`LuiConfigurationException`). The read path by name does not compare the fingerprint — it names no type; the
    format of the registry's serializer for `T` is what reads the bytes.

11. **A payload type is one concrete type.** `object`, an interface and an abstract class stand for many types, so an index of one would hold
    the payloads of any type — the untyped payload under another name (a source-generated context answers for `System.Object` whatever it
    lists, so the registry alone would let it through). `LuiIndexDefinition<TKey, TPayload>` and `LuiLinkedIndexDefinition<TKey, TPayload>`
    with such a `TPayload` are a `LuiConfigurationException` naming the definition at the first `Create`/`Index`, and `Use<TPayload>` with one
    is refused at the registration — also an explicit serializer for `object`: it would be the catch-all registered by hand. The rule is
    about the declared type, not about what a value is: a sealed envelope that holds the variants is a concrete type and is fine. Arrays,
    structs, enums and sealed or open (non-abstract) classes are allowed.
12. **An index that stores a fingerprint is written only through its definition.** An index opened by name (`catalog.Index(name)`) whose stored
    definition holds a payload type name and a format reads as the catalog reads, and **refuses every write of a payload** with a
    `LuiConfigurationException` that says to write through `catalog.Index(definition)` — neither the serializer of the old calls nor the
    registry by runtime type puts bytes of another format into it (§9). A failure of a context to describe a type is kept as a record like
    every other and thrown anew, naming the definition.

## Alternatives

| | For | Against |
|---|---|---|
| **A — one serializer per catalog, as before** | nothing to change | two formats in one file are impossible; no fingerprint |
| **B — a serializer per index** (a parameter of the definition) | the serializer is where the index is declared | the serializer is a property of the *type*, not of the index: two indexes of one type would repeat it, and the read path by name would need it per call |
| **C — a registry, bound to the type** (this decision) | one place, the type decides; two formats in one catalog; the fingerprint has the type to compare | a registry to build once |
| **D — a catch-all in the registry** (`Use(ILuiPayloadSerializer)` for every type) | the shortest start | the owner rejected it: the file would say nothing about which format a type has, and a type that nobody registered would pass |

## Consequences

- **Breaking, before 1.0.0** (0071): a payload definition needs a registry (`Payloads = new LuiPayloadRegistry().Use(context)`) — a typed
  definition on a catalog that has only `PayloadSerializer` is a `LuiConfigurationException` at `Create` or `Index`; setting both options is
  refused. `PayloadSerializer` and the non-generic serializer go with the old calls in #268. The CHANGELOG has the migration note.
- **LUI format 2 holds two more fields**, written only for an embedded index with a payload; the schema of an index without payload is
  unchanged (the golden test stays).
- **A reflection-free lookup.** A context is asked for the generated description of the type (`TryGetTypeInfo`), so the registry is safe for
  trimming and AOT. A context also knows the types its listed types are made of (`int`, `string`): they resolve without an entry.
- **The hot paths are unchanged**: `catalog.Index(definition)` stays a lookup (no allocation, no command); a typed write calls the serializer
  it holds; `GetPayload<T>()` on the read path by name is one dictionary lookup by type and the serializer.
- **What the file still does not say**: the shape of the payload. The fingerprint checks identity — the type name and the format —,
  not fields; adding, renaming or removing a field is the business of the serializer and the application. How payloads are upgraded
  is part of the migration topic (#262).
- **The sample and the documentation** (#263) use `[LuiPayloadName]` on every payload type and say why.
