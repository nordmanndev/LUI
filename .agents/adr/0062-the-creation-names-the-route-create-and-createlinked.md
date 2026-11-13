# 0062 — The creation names the route: `Create` and `CreateLinked`

- **Status:** Accepted (owner's decision, 2026-11-13)
- **Date:** 2026-11-13
- **Builds on:** [0024](./0024-the-api-polish-before-the-freeze.md) (point A.3 of the review of 2026-11-04: the route is
  named in the call), [0033](./0033-the-read-path-opens-an-index-without-writing.md) (the catalog is the entry point and it
  creates), [0035](./0035-the-index-knows-no-types.md) (the index knows no types),
  [0026](./0026-the-public-api-from-0-1-0-on-grows-only-additively.md) (the freeze)
- **Affects:**
  - `LuiCatalog.Create` — creates an **embedded** index; `LuiCatalog.CreateLinked` — creates a **linked** index. The
    linked overload of `Create` is gone
  - `docs/api.md`, `docs/tutorial/02-your-own-data.md`, `docs/tutorial/03-the-payload.md`, `README.md`
  - `tests/Nordmann.LUI.Architecture.Tests/PublicApiShapeTests.cs` — the guard
  - `CHANGELOG.md` — the migration note
- **Issue:** #145 (epic #117, and the point A.3 of epic #39)

## Context

Point **A.3** of the independent review of 2026-11-04: `AddIndex` and `AddLinkedIndex` instead of two overloads chosen by
the lambda's return type. The reason the owner gave: **the route is named in the call, not guessed by the compiler** — a
linked description handed to `AddIndex` does not compile any more
([0024](./0024-the-api-polish-before-the-freeze.md)).

The role of the builder of the indexes went with the entry point: the catalog is the entry point
([0033](./0033-the-read-path-opens-an-index-without-writing.md)), the container holds the catalog and nothing else, and the
creation moved into the core — `LuiCatalog.Create(name, index => …)`, **two overloads, one per content source**, and the
compiler picks by the return type of the lambda.

That is safe: a linked description can only bind to the linked overload, so nothing is chosen silently. But the route is
not named in the call any more, and the decision of 2026-11-04 asked for it. The naming lived on the side of **opening**:
`catalog.Index(name)` and `catalog.LinkedIndex(name)`.

Before the tag `v0.1.0` the change is free ([0026](./0026-the-public-api-from-0-1-0-on-grows-only-additively.md)):
nothing is published, and the compiler finds every call site. After the tag it would be `0.2.0` with a migration note.

## Decision

**The creation names the route: `LuiCatalog.Create` creates an embedded index, `LuiCatalog.CreateLinked` creates a linked
one.**

1. **The linked overload of `Create` is gone.** `Create` takes a description that returns a `LuiEmbeddedIndexBuilder`;
   `CreateLinked` takes one that returns a `LuiLinkedIndexBuilder`.
2. **The compiler does not choose the route.** The name in the call says what is created — as it does on the side of
   opening (`Index` / `LinkedIndex`).
3. **Everything that creates a linked index follows**: the library, the tests, the sample, the benchmarks, the tutorial,
   the README and the API page.
4. **The guard follows.** `The_catalog_creates_an_index_and_opens_one_that_is_there` says that `Create` is one method
   that creates an embedded index, and that `CreateLinked` is one method that creates a linked one.

## Consequences

- **The point A.3 of the review is carried.** The route is named in the call, and a linked description cannot be handed to
  the wrong method: it does not compile.
- **A breaking change, and still free.** The freeze of [0026](./0026-the-public-api-from-0-1-0-on-grows-only-additively.md)
  begins with the tag `v0.1.0`, and there is no such tag yet. The migration note is in the `CHANGELOG`.
- **The surface of 0.1.0 is one name shorter and one longer**: `Create` and `CreateLinked` instead of `Create` twice. The
  table of the surface — every point of the review of 2026-11-04 and the member that carries it — is written down in
  issue #144.
- **The symmetry is complete:** `Create` / `CreateLinked` to create, `Index` / `LinkedIndex` to open.

## Alternatives

| | For | Against |
|---|---|---|
| **A — `Create` and `CreateLinked`** (this decision) | the route is named in the call; a linked description cannot be handed to the wrong method | two names where one would do, and the API of a library grows a member |
| **B — `Create` twice, chosen by the lambda** | one name; the compiler picks the overload that fits the description | the route is guessed by the compiler — what the review of 2026-11-04 asked to stop |
| **C — `CreateEmbedded` and `CreateLinked`** | both routes named | the common case — an embedded index — loses the short name for nothing |
