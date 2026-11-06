# Active task

<!--
What the next session is doing. One task, in the words of the issue that
describes it. Link the issue; do not repeat the whole plan here.
-->

**Issue:** [#58](https://github.com/nordmanndev/LUI/issues/58) — the index knows no types: the serialiser belongs to
the catalog, the type stands where the payload is used. Variant **C** of the owner's discussion of 2026-11-06; the typed
view on top (variant D) is [#59](https://github.com/nordmanndev/LUI/issues/59), only when users ask for it.
**Branch:** `refactor/the-index-knows-no-types`, from `main` at `73ef344` (the merge of PR #57).
**Decision:** [ADR-0035](../../docs/adr/0035-the-index-knows-no-types.md).

## What is done

- **The library**: no public type of an index has a type parameter (`ILuiIndex`, `ILuiEmbeddedIndex`, `ILuiLinkedIndex`,
  `LuiEntry`, `LuiHit`, `LuiResult`, `LuiQuery`, the builders). New: `LuiKey` (number or text), `LuiKeyKind`,
  `ILuiIndex.KeyKind`, one `ILuiPayloadSerializer` per catalog (`LuiCatalogOptions.PayloadSerializer`,
  `LuiPayloadSerializer.Json(context)`), `hit.GetPayload<T>()` / `TryGetPayload<T>` / `HasPayload`,
  `LuiPayloadException`, `catalog.Index(name)`, `catalog.LinkedIndex(name)`, `catalog.LinkedIndex(name, map)`. Gone:
  `LuiNoPayload`, the typed serialisers, `LuiEmbeddedIndexBuilderExtensions`, `IndexDefinition.Payload` and
  `.PayloadType` (an old definition with them is read, the fields are ignored).
- **The callers**: the sample (each wrapper says how it reads its payload — `SamplePayload`), the tests (the test catalog
  gets the chip serialiser by default, `TestDatabase.Open`), the benchmarks.
- **The documents**: ADR-0035 (with the alternatives A–D and the measurement), a note on ADR-0033, the ADR index, the
  spec (§7.1, §7.3 — what a linked index keeps and reads live —, §8.3, §12.1–§12.9), the README (the payload example is
  run by the smoke test: a block that starts with `// The types of the application` goes after the statements), the
  CHANGELOG with the migration list, `docs/testing.md`.
- **Measured**: the allocation per query (200,000 entries, 31 rounds, median) — no payload 33.66 → 34.27 KB, a payload not
  read 38.43 → 35.52 KB, all hits read 38.90 → 39.21 KB.

## Open

- The pull request is opened when the owner says so.

## The next step

After #58: **#38** (the budget table of the README guarded against §16), then the **release** (PR-012, `0.1.0`) — which
the owner has to ask for; after that #35, #31, #32, #44, #59 (only on demand) and the `compactLevel` follow-up of #48.
