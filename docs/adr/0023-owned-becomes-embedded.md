# 0023 — `Owned` becomes `Embedded`: the name of the content source in LUI's own table

- **Status:** Accepted (owner's decision, 2026-11-04, after an independent architecture review)
- **Date:** 2026-11-04
- **Issue:** #41, step 2 of epic #39 (the public API before the freeze of 0.1.0)
- **Supersedes:** the *name* `Owned` in ADRs 0001, 0006, 0009, 0011–0017, 0020 and 0021 — their decisions stand; each one
  has a note that points here
- **Affects:**
  - `ILuiOwnedIndex` → `ILuiEmbeddedIndex`, `LuiIndexBuilder` → `LuiEmbeddedIndexBuilder`,
    `LuiIndexBuilderExtensions` → `LuiEmbeddedIndexBuilderExtensions` (public)
  - `OwnedIndex`, `OwnedSource`, `OwnedSync`, `OwnedCheck` → `Embedded…` (internal)
  - `lui_meta.source`: `Owned` → `Embedded` (**stored**)
  - `tests/Nordmann.LUI.Architecture.Tests/Public_API_Shape.cs`, `tools/mutation-check.sh`, the golden file
    `golden/owned-schema.txt` → `golden/embedded-schema.txt`
  - spec §5 and everything that names the source, README, CHANGELOG, `docs/domain/lui.md`, `docs/testing.md`

## Context

LUI has two content sources (spec §5):

- the one in a table **of the library**, `lui_<index>`, which the application writes through the index — called `Owned`,
  the default;
- the one in a table **of the application**, which LUI reads — called `Linked`.

An independent architecture review of `main` (2026-11-04) found that `Owned` is the wrong word, and the owner agreed:

1. **It collides with the vocabulary of .NET.** "Owned" says *lifetime*: Autofac's `Owned<T>` is an instance its consumer
   disposes; an "owned" `IDisposable` is one that a class disposes with itself. `ILuiOwnedIndex` is the opposite: a
   singleton, never disposed. The name promises what the type does not do.
2. **It names no object.** "Owned" is a participle without a noun; only the specification says that the *content table*
   is meant — not the index, not the data, not the lifetime.
3. **It points at the wrong party.** It suggests that the application is responsible for the index. The catalog owns the
   tables.
4. **The pair does not name one dimension.** `Linked` says where the rows are (in a table that LUI points to); `Owned`
   says who is responsible.

Nothing is published (neither package exists on nuget.org), so the change costs a diff and no consumer.

## Decision

The content source in LUI's own table is called **`Embedded`**: it names the place of the rows (inside the database of the
application, in a table of the library) and pairs with `Linked`, which names the pointer to a foreign table.

| Before | After |
|---|---|
| `ILuiOwnedIndex<TKey, TPayload>` | `ILuiEmbeddedIndex<TKey, TPayload>` |
| `LuiIndexBuilder<TKey, TPayload>` | `LuiEmbeddedIndexBuilder<TKey, TPayload>` |
| `LuiIndexBuilderExtensions` | `LuiEmbeddedIndexBuilderExtensions` |
| `OwnedIndex`, `OwnedSource`, `OwnedSync`, `OwnedCheck` | `EmbeddedIndex`, `EmbeddedSource`, `EmbeddedSync`, `EmbeddedCheck` |
| `lui_meta.source = 'Owned'` | `'Embedded'` |
| spec §5 "eigenständig" | "eingebettet" |

- **The builder is renamed with the index.** `LuiIndexBuilder` was the builder of the embedded route, and the name did not
  say so next to `LuiLinkedIndexBuilder`. `LinkTo` stays an extension of `LuiEmbeddedIndexBuilder<long, LuiNoPayload>` —
  "turn an embedded description into a linked one" — because that keeps the type state of ADR-0013: a linked index has a
  `long` key and no payload of its own. A plain method would give that up.
- **The retired word is guarded.** `Public_API_Shape.The_content_sources_are_called_Embedded_and_Linked_…` fails when a
  public type, member or parameter of either package contains "Owned". The mutation
  `the-old-word-returns-to-the-public-surface` (`tools/mutation-check.sh`) shows that it bites.
- **The ADRs are history.** They keep the word they were written with, and each one that uses it has a note at the top.
  The delivery plan keeps the names of its time and has the same note. The specification (the contract), the README, the
  CHANGELOG and the living docs are renamed.

### Alternatives that were rejected

| Name | Why not |
|---|---|
| `Standalone` | The German gloss of the old spec ("eigenständig"), but wrong: the index shares the database of the application. |
| `Managed` | Collides with "managed code"; says who is responsible, not where the rows are. |
| `Internal` | A C# keyword, and "not public" in the other reading. |
| `Native` | Collides with Native AOT. |
| `Local` | Suggests a local file; the rows are in the application's database. |
| `SelfContained` | Too long, and it reads as "has no dependencies" — another topic. |

## Consequences

- **A breaking change, and a free one.** There is no consumer. After 0.1.0 it would be impossible (epic #39).
- **Databases that a pre-release build created are refused, not migrated.** `lui_meta.source` holds `Owned` there.
  `Ensure` answers with a `LuiDefinitionMismatchException`, and `DropIndex` refuses a source it does not know
  (ADR-0014, N2) — so the way out is to delete the file. A legacy branch for a value that no release ever wrote would stay
  in the code for good, and the freeze (epic #39) makes "for good" literal.
- **A diff of 233 replaced lines and ten renamed files**, made as a rename and nothing else, so that it reads as one:
  files with `git mv`, the identifiers by a script, the specification and the CHANGELOG by hand (the German gloss, and a
  sentence about earlier pre-release names). The only additions are this record, the name notes, the guard and its
  mutation.
- **`AddIndex` still chooses its overload by the return type** of the description (`LuiEmbeddedIndexBuilder` or
  `LuiLinkedIndexBuilder`). The two names are now readable in the signature; splitting them is #42.

**Verified by:**

- `EmbeddedIndexTests` — `lui_meta.source` is `Embedded`;
- `SchemaGoldenTests` — the schema is unchanged, byte for byte (`golden/embedded-schema.txt`);
- `Public_API_Shape` — the guard above;
- the mutation `the-old-word-returns-to-the-public-surface`;
- the package smoke test (the README is compiled and run with the renamed API).
