# 0026 — The public API from 0.1.0 on grows only additively

- **Status:** Accepted (owner's decision, 2026-11-04, epic #39 step G)
- **Date:** 2026-11-04
- **Issue:** #43, the last step of epic #39 (the public API before the freeze of 0.1.0)
- **Builds on:** [0018](./0018-two-packages-core-and-hosting.md) (two packages, one version), [0023](./0023-owned-becomes-embedded.md),
  [0024](./0024-the-api-polish-before-the-freeze.md) and [0025](./0025-the-typo-dictionary-only-grows.md) (the changes before the freeze)
- **Tightens:** the sentence of ADR-0018 that "in 0.x every minor version may break the API (SemVer)"
- **Affects:** `README.md` (Status), `CHANGELOG.md`, `AGENTS.md` (a *Never*), `docs/architecture/alignment.md`, `.agents/adr/0018-…md` (a note)

## Context

Neither package exists on nuget.org yet. Until they do, a breaking change in the public API costs nothing; afterwards it
costs every application that has taken a dependency. An independent architecture review of `main` (2026-11-04) listed what
should change while it is free, and the owner went through it point by point (epic #39). Those changes are done:
[0023](./0023-owned-becomes-embedded.md) (`Owned` → `Embedded`), [0024](./0024-the-api-polish-before-the-freeze.md) (the API
polish) and [0025](./0025-the-typo-dictionary-only-grows.md) (the typo dictionary, which carried ten public members that the
freeze would otherwise have pinned).

SemVer allows every minor version of a `0.x` line to break. That is true, and no promise to anyone. This record makes one.

## Decision

1. **From the tag `v0.1.0` on, the public API of `Nordmann.LUI` and `Nordmann.LUI.Hosting` changes only additively.** A new
   type, a new method, a new overload that does not make an existing call ambiguous, a new `init` property. **A new optional
   parameter on an existing method is not additive:** it changes the signature in the compiled assembly, so a package or
   an application built against 0.1.0 meets a `MissingMethodException`, and a method group stops converting. Such a setting
   comes as a new overload (the old one stays and calls the new one).
2. **A breaking change** — a removed or renamed type or member, a changed signature, a changed meaning of a documented
   behaviour — **needs a new minor version** (`0.2.0`, not a patch), **a migration note in the `CHANGELOG`** (what to write
   instead) **and an ADR** that says why it was worth it. After 1.0.0 the same rule needs a major version.
3. **What counts as the public API:** the public and protected members of the types the two assemblies export, and the
   behaviour the specification documents for them. The internals (`internal` types, the layout of the source tree) and
   the benchmark and test projects are not covered. **The line:** until the tag exists, `main` may still change a name — but
   not without an ADR, as ever; from the tag on, decisions 1 and 2 apply.
4. **Interfaces.** `ILuiIndex`, `ILuiEmbeddedIndex` and `ILuiLinkedIndex` are there to be *called* (and injected, and replaced
   by a test double). `ILuiSimilarity` and `ILuiPayloadSerializer<TPayload>` are there to be *implemented* by applications.
   - **A new member of an interface is added as a default interface member** — then nobody who implements the interface
     has to change anything, and it is additive.
   - **Where there is no sensible default, it is not added** without the full rule of decision 2: a new abstract member
     breaks every implementer, however few they are, and so needs a minor version, a migration note and an ADR. A `CHANGELOG`
     line alone is not enough.
   - The three interfaces of the indexes are held by a guard (`Public_API_Shape`): it pins the member list of the read
     interface `ILuiIndex` and says that the write interfaces carry the maintenance members. A new member of `ILuiIndex`
     therefore edits that list **in the same pull request** — on purpose: it is the speed bump in front of the surface a
     suggest endpoint is injected with.
5. **Enums and records.**
   - **Enums:** a new value is appended; the numbers of the existing values never change (`LuiPriority` is packed into the
     row id of every entry; an application may cast or compare the others). A new value is additive in the API, but an application's exhaustive `switch`
     may meet it — so it is a `CHANGELOG` line that says "adds `X` to `Y`", and applications are asked to keep a `default`.
   - **Records:** a new property is an `init` property. A new positional parameter changes the constructor and the
     `Deconstruct` and is a break. **`Equals`, `GetHashCode`, `ToString` and deconstruction of the records are not part
     of the promise:** a new property changes them, and an application that asserts on them in its tests will notice.
6. **ADR-0018's exact pin stays** (owner's decision, 2026-11-04). `Nordmann.LUI.Hosting x.y.z` depends on `Nordmann.LUI [x.y.z]`.
   The pin was chosen because "in 0.x every minor may break" and a direct reference of a newer core would have met a
   changed signature at run time; this record withdraws that premise, and the pin stays for another reason: the two packages
   release together, so a fix of either releases both, and one version of both is what is tested. Its cost, measured in
   ADR-0018, stays and is accepted: an application that references `Nordmann.LUI 0.1.1` directly beside
   `Nordmann.LUI.Hosting 0.1.0` gets `NU1608` (an error with `TreatWarningsAsErrors`) although the combination would be
   safe. Relaxing it to `>= x.y.z, < x.(y+1).0` was considered and **not done**; it is a change of ADR-0018 and gets its own ADR.
7. **The first release stays `0.1.0`** (owner's decision). The promise is made at that tag, not at 1.0.0.
8. **Where it is written down for contributors:** `AGENTS.md` gets the rule as a *Never* (it is the file a contributor reads
   first), and `README.md` and `CHANGELOG.md` say it in the words of this record. There is no gate that checks the tag; the
   guards of the shapes (`Public_API_Shape`, the mutation check) and review hold the line.

### The points of the review that are not done — by name

Without this list the review of 2026-11-04 and the freeze would sit side by side in the repository, and a later contributor
could take up a rejected point and break the promise. Each of the first four is possible only with a new minor version
and a migration note (decision 2). The reasons are those of the owner, recorded in epic #39.

| Review # | Proposal | Decision | Why |
|---|---|---|---|
| A.2 | `LuiServicesBuilder` → `LuiIndexRegistrations` | **Not done.** The name stays. | `AddX()` returning a `…Builder` that collects registrations is the .NET convention (`IMvcBuilder`, `IHealthChecksBuilder`, `IHttpClientBuilder`). |
| A.4 | `LuiPriority.Elevated` → `Medium` | **Not done.** The enum stays as it is (`High` = 1, `Elevated` = 2, `Normal` = 3, …). | "Medium" ≈ "Normal" moves the confusion one step down. Quick to change if it really confuses anyone — but then a minor version. |
| B.1 | one sync type for both routes | **Not done.** Two types: `LuiSyncReport` (embedded: a diff against a source) and `LuiLinkedSyncReport` (linked: a rebuild from the table). | They are different operations; one type would have half of its fields always 0. (`LuiSyncResult` became `LuiLinkedSyncReport` in ADR-0024.) |
| B.2 | `With…` methods on `LuiQueryOptions` | **Not done.** | They make the surface larger; `options with { … }` exists. The drift risk is covered by a guard: every property of `LuiQueryOptions` has its method on `LuiQuery`, and the other way round. |
| C.4 / C.5 | keyed registration of the internal options and registrations | **Not done.** | A keyed service with a magic key is a half-public contract; every hosted service is visible as a `ServiceDescriptor` anyway, so it hides nothing. |
| D.2a | name the hosting switches in the README | Already done (#19). | — |
| D.3 | end `LuiTypoRebuilder` when there is nothing to do | **Moot.** | The reason it was not done (a round per minute costs practically nothing) held; then [0025](./0025-the-typo-dictionary-only-grows.md) removed the service altogether, with `Stale` and `RebuildStaleTypoDictionariesEvery`. |

### Not part of the freeze, and why

- **#35** (the first dictionary build inside the query's transaction): owner's decision, after 0.1.0. It is not an API change.
- **E.1** (an own Jaro-Winkler that would drop `F23.StringSimilarity`): goes with #30, after its profiling. **E.2** (an own
  SymSpell): a separate issue, after 0.1.0. A dependency is not part of the API, but changing one needs an ADR (AGENTS.md).
- **`compactLevel` of the typo dictionary** (#48): an internal setting; memory against latency, a follow-up.
- **A guard that compares the public API with a committed snapshot** was offered and **not taken** (owner, 2026-11-04). The
  guards that exist (`Public_API_Shape`, the mutation check) cover shapes, not signatures. `Public_API_Shape` pins the member
  list of `ILuiIndex` and so is a partial snapshot for that one type; for every other type a change that adds, removes or
  re-signs a public member is caught by review. If it turns out to slip through, a snapshot test is the next step, and it is cheap.

## Consequences

- **A name that is wrong now is a name for the whole 0.x line** — unless the cost of a minor version with a note is worth it.
  That is the point of doing the list above *before* the tag.
- **The sample (PR-011) is written against the final API**, and so is the first README that people see on nuget.org.
- **Extending is cheap, removing is not.** A new setting is a new overload or a new method; a new result field is a new `init`
  property of a record. Things that would have to change an existing signature wait for a minor version.
- **`CHANGELOG`:** every entry that moves the public API says whether it adds or breaks; from 0.1.0 on a "breaks" entry
  carries its migration note.
- **Not covered:** the schema of the `lui_*` tables and the `lui_meta` definition are the library's own (an application never
  touches them, AGENTS.md); a change that needs an old database to be recreated gets an ADR of its own, as ever.
