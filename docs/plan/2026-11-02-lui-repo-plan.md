# LUI — repo, package and sample plan

> **Name note (ADR-0023, 2026-11-04):** this plan says `Owned` where the code now says `Embedded`; it keeps the names of its time.

- **Status:** Proposed
- **Date:** 2026-11-02
- **Inputs:** `LUI-Spezifikation.md` (Entwurf 2, 2026-11-01) · `nordmanndev/standards` @ `c63d696` (v2.5.5)
- **Scope:** the new `nordmanndev/LUI` repo, one NuGet package, one sample app

---

## 1. Where we start from

| Fact | Verified how |
|---|---|
| `nordmanndev/LUI` is **empty** — 0 KB, branch `main`, no commits, no other refs | `git clone` → "warning: You have cloned an empty repository"; `git ls-remote` → no refs |
| The previous LUI (roaring-bitmaps, snapshot format v12, 57 ADRs, 433 tests) is **gone from the remote** | same as above — nothing to carry over except the *working method* (spec §17.4) |
| `nordmanndev/standards` is the reference baseline | cloned to `/workspace/repos/standards` @ `c63d696` |
| The standards already name **`flexdatalist` 3.x** as *the* browser autocomplete library (Tier 1, `architecture/stack.md`) | read `architecture/stack.md` |
| The standards' browser baseline is Alpine.js + Tailwind 4, **no bundler**, ES modules from CDN | `architecture/stack.md` §Browser runtime, `examples/frontend-stack/` |
| Sandbox ships .NET **9.0.316**; .NET **10.0.403** was installed to `/workspace/dn10` for this work | `dotnet --list-sdks` |

**Consequence:** this is a greenfield build. The spec is the contract; nothing in it has been implemented yet. The repo's own history starts here.

---

## 2. Feasibility probes — the spec's core mechanics, tested today

I did not take the spec's schema on faith. Six probes ran against **the exact dependency set the plan will ship** (`Microsoft.Data.Sqlite` 10.0.14 on .NET 10.0.403):

| # | Probe | Result |
|---|---|---|
| P1 | `select sqlite_version()` | **3.53.3** — matches the spec's tested version (§17.3) |
| P2 | `fts5(..., tokenize='trigram remove_diacritics 1')` | **OK** — the tokenizer the whole design rests on |
| P3 | `GENERATED ALWAYS AS (...) STORED`, incl. *chained* generated columns (`s_compact` → `s_norm`) | **OK** — the spec's §7.2 schema is implementable as written |
| P4 | Bit-packed rowid `(priority << 61) \| ((2²⁹−1 − weight) << 32) \| doc_id` | **OK** — and `ORDER BY rowid` returns *highest weight first*, exactly as E2 claims |
| P5 | `EXPLAIN QUERY PLAN` of the candidate query | **`SCAN f VIRTUAL TABLE INDEX 64:M1`** + **`SEARCH c USING INDEX …_rowid`** — no `TEMP B-TREE`. The spec's plan assertion (§19) holds |
| P6 | `Linked` mode: `ALTER TABLE app_table ADD COLUMN … GENERATED … STORED`, FTS5 `content_rowid` pointing at a **generated** column, the 3 triggers, `rebuild`, `integrity-check` | **OK** — data preserved, triggers keep the index in sync, `integrity-check` clean |

**One correction to the record:** my first probe run *failed* with `near "STORED": syntax error`. That was **my own missing parenthesis** in the throwaway SQL, not a SQLite limitation — probe P3 (fixed) proves chained generated columns work. Worth stating, because the failure looked exactly like a spec defect.

**What the probes did not settle:** the SQL ↔ C# normalizer parity (§6.2, §19) is the one claim that cannot be verified without the German word list. It is the top risk of the whole build — see §11.

---

## 3. Decisions

These are locked. Each one is an ADR in `docs/adr/`.

### D1 — One NuGet package: `Nordmann.LUI`

The spec §17.1 proposes three packages (`Nordmann.LUI.Abstractions`, `Nordmann.LUI`, `Nordmann.LUI.DependencyInjection`). The delivery requirement is **one**. Resolution:

- **One assembly, one package id: `Nordmann.LUI`.**
- The spec's **namespace layout survives intact** (§17.2) — that is what consumers actually see:
  `Nordmann.LUI` · `.Indexing` · `.Querying` · `.Normalization` · `.Typos` · `.Similarity` · `.Payloads` · `.DependencyInjection`
- The DI extension (`AddLui`) lives in namespace `Nordmann.LUI.DependencyInjection` inside the same assembly.

**Cost, stated honestly:** every consumer pulls `Microsoft.Data.Sqlite` + `symspell` + `F23.StringSimilarity` + `Microsoft.Extensions.*`, even if it only wants `Normalizer`. All four are permissive MIT/Apache-2.0. At NuGet's package-graph level this is cheap; at the *assembly* level it means a consumer cannot trim the SQLite dependency out of a trimmed app.

**Why not split later?** Because splitting is a *mechanical* refactor later (move types, add a package reference) but an *API-breaking* one now. Starting with one package and splitting later is the cheap direction.

### D2 — Repo shape

```
nordmanndev/LUI/
├── README.md                    ← the front door (see §7)
├── AGENTS.md                    ← project contract; inherits the standards, thin delta
├── CHANGELOG.md                 ← Keep-a-Changelog; required on public API change
├── LICENSE                      ← MIT, "Copyright (c) 2026 Nordmann" (as the standards)
├── Directory.Build.props        ← the standards' exact block (net10.0, nullable, warnings-as-errors, analyzers)
├── Directory.Packages.props     ← central package management — the only place versions live
├── .editorconfig
├── .gitignore
├── .github/
│   ├── ISSUE_TEMPLATE/          ← task / bug / adr (from the standards)
│   └── workflows/
│       ├── ci.yml               ← build · test · arch-test · format · pack · smoke-test
│       └── publish.yml          ← v* tag → nuget.org (owner-gated)
├── docs/
│   ├── adr/                     ← ADR-0001… (the spec's E1–E10 become ADRs)
│   ├── benchmarks.md            ← published numbers for the §16 targets
│   └── plan/                    ← this file
├── src/
│   └── Nordmann.LUI/            ← THE package (one assembly)
│       ├── Extensions/          ← ServiceCollectionExtensions.cs (standards: every project ships one)
│       ├── Catalog/             ← LuiCatalog, LuiCatalogOptions
│       ├── Indexing/            ← schema, triggers, Ensure, Sync, Check, Rebuild
│       ├── Querying/            ← candidate selection, ranking, prefix path
│       ├── Normalization/       ← Normalizer, SQL expressions, highlight mapping
│       ├── Typos/               ← prefix SymSpell dictionary
│       ├── Similarity/          ← JaroWinkler, NgramDice, DamerauLevenshtein, Levenshtein
│       ├── Payloads/            ← serialization, column mapping
│       └── DependencyInjection/ ← AddLui(), keyed services, hosted ensure
├── tests/
│   ├── Nordmann.LUI.Unit.Tests/       ← the rules, without a database (xUnit · Shouldly)
│   ├── Nordmann.LUI.Integration.Tests/← the library against SQLite
│   ├── Nordmann.LUI.Architecture.Tests/← the shape of the repository
│   ├── Nordmann.LUI.Testing/          ← the shared setup (a library, no tests)
│   ├── Nordmann.LUI.TestData/         ← the corpora (no project)
│   ├── Nordmann.LUI.Architecture.Tests/← NetArchTest rules (standards mandate)
│   └── Nordmann.LUI.TestData/         ← corpus + fixtures shared by tests & benchmarks
├── benchmarks/
│   └── Nordmann.LUI.Benchmarks/  ← BenchmarkDotNet + the 3M-corpus generator (§16)
├── samples/
│   └── Lui.Sample/              ← ASP.NET Core minimal API + the inline static site
└── tools/
    └── fetch-wordlist.sh        ← the full 685k-word German list (opt-in)
```

**Why `samples/` and not `src/`:** the standards' `src/` layout is for the application's own layers. `Lui.Sample` is not part of the package and is not shipped; it is the demo. Keeping it out of `src/` keeps the package graph honest — `dotnet pack` cannot accidentally pick it up.

### D3 — Standards alignment: what applies, what does not

This is a **library**, not a product. The standards' non-negotiable rules target products; applying all of them to a NuGet library would produce a worse library. Here is the line I draw, and why.

**Applies (and is enforced mechanically):**

| Rule | How it lands here |
|---|---|
| `Directory.Build.props` block verbatim | net10.0, nullable, `TreatWarningsAsErrors`, `EnableNETAnalyzers`, `AnalysisLevel=latest-recommended`, `NuGetAudit` |
| Central package management | `Directory.Packages.props`; **no `.csproj` may carry a `Version` on a `PackageReference`** — arch test |
| `Meziantou.Analyzer` from day one | analyzer package in `Directory.Packages.props` (standards entry 9 — "adopted from day one") |
| Test pyramid: unit + architecture + integration | xUnit + Shouldly + Alba + NetArchTest, three test projects |
| ADR discipline | `docs/adr/NNNN-slug.md`, format `# NNNN — Title` / Status / Date / Supersedes / Affects / Context / Decision / Consequences |
| `CHANGELOG.md` on public API change | standards' rule, unchanged |
| Closed-library policy | ADR for every dependency that is not in the standards' catalogue (see D4) |
| `AGENTS.md` at repo root | inherits the standards' contract, states the project-level delta only |
| README shape | "What this is / what this is not / quick start / install / API map / license" — the standards' own README structure |

**Does not apply (with the reason):**

| Standards rule | Why it does not apply to a library |
|---|---|
| DDD aggregates, CQS handlers, Mediator dispatch | LUI has no domain and no handlers. `LuiCatalog` → `ILuiIndex` → `Query` is a **library API**, not a command bus. Forcing Mediator in would add a source generator to every consumer's build for no behaviour. |
| OrmLite persistence layer | LUI *is* the persistence layer, over raw `Microsoft.Data.Sqlite`. A second ORM would hide the SQL that the whole design depends on (the spec's §8 is explicit SQL). |
| Outbox / background worker / integration events | LUI has no events. The one long-running operation (`Sync`) takes an `IProgress<T>` and a `CancellationToken` — a background job's shape, not a queue's. |
| ULID identifiers | The spec fixes `TKey` as `long` or `string` (§12.3) — the *application's* key, not LUI's. LUI must not impose an ID type on its consumer. |
| Serilog → Seq as the logging baseline | A library must not force a logging stack on consumers. LUI takes an optional `ILogger` and nothing more; the **sample** may use the built-in `ILogger`. |
| Multi-tenant by default | LUI opens *one* database per catalog. Tenancy is the application's business. `Linked` mode exists precisely so an app can keep its data where it lives. |
| Alpine.js + Tailwind frontend baseline | Applies to *project* frontends. The sample is a **library demo**: its job is to expose the LUI API, not to model a production frontend. See D7. |

### D4 — Dependencies, and the one closed-library deviation

The spec §17.3 names three dependencies. The standards' `architecture/stack.md` "closed list" (ADR-0008) does **not** list `symspell` or `F23.StringSimilarity`.

→ **ADR required**, naming both, with the licensing and the alternatives considered. `Microsoft.Data.Sqlite` is already in the standards' stack (it is OrmLite's provider), so it needs no exemption.

One more dependency decision: the standards say "never add a NuGet dependency not listed" — and `symspell` is a *dictionary* library, `F23.StringSimilarity` a *distance* library. Both are MIT and actively maintained. The alternative (hand-rolling SymSpell) is a non-starter for a 1.0. The deviation is recorded, not hidden.

### D5 — The nine open questions (§21 O1–O9) are deferred, each with an ADR

1.0 ships **exactly** the decided surface of the spec. Each of O1–O9 gets a one-page ADR: the question, the deferral, and the **trigger** that would revive it (e.g. O6 "a `Linked` consumer needs `published = 1` filtering → add a `WHERE` clause to the triggers"). A deferred question with a trigger costs one page; a deferred question without one costs archaeology.

This keeps the API surface honest: nothing in the README is aspirational.

### D6 — The test corpus: curated in-repo, full list on demand

The spec §19 demands SQL ↔ C# parity over **all 685,829 words** of the German word list. That list is not in the repo, and a CI job that fails because an external URL moved is a test that measures the wrong thing.

- `tests/Nordmann.LUI.TestData/wordlist/` — a **checked-in curated corpus**: the spec's edge cases (ä/ö/ü/ß, `ẞ`, `İ`, `Ł`, `Łódź`, `Straße`, `Café Crème`, `Apfel-Zimt-Kuchen`, `#Hashtags`, `ß`/`ss` pairs) plus a few thousand common German words covering the letter combinations that stress the transliteration.
- `tools/fetch-wordlist.sh` — pulls the **full** list for the complete parity run; documented in the README and in `docs/benchmarks.md`.
- CI runs the curated corpus on **every** PR (deterministic, seconds) and the full list on a schedule/manual dispatch.

### D7 — The sample app

> **Superseded in part by [ADR-0027](../adr/0027-the-sample-flexdatalist-modular-client-one-fetch-boundary.md) (2026-11-04):** the page is no longer one inline `index.html`
> but ES modules by concern; the plan's `Highlight` knob and highlight column do not exist (LUI has no `Highlight`: struck, ADR-0028); the routes are plural; there is no
> list of entries and no `POST` form of the suggest route. What the plan wrote below is history.

`dotnet run --project samples/Lui.Sample` → an **inline static site** served by the ASP.NET Core app, with the full LUI API behind it.

**Shape:** one `wwwroot/index.html` with inline `<style>` and an inline `<script type="module">`; `wwwroot/assets/js/flexdatalist.es.js` vendored (flexdatalist 3.1.1, MIT, pinned, with provenance + license header).

**"flexdatalist js only version"** = the JS build, **without** the library's CSS — the sample ships its own styles (the standards' ink/brand tokens, hand-written). That is also why the sample is vanilla JS rather than Alpine: the demo's subject is the **LUI API**, and a reactivity framework would add a dependency without teaching anything about LUI. The deviation from the standards' Alpine baseline gets an ADR.

**The page must show the whole API, not just a search box:**

| Region | What it proves |
|---|---|
| The suggestion input (flexdatalist, `url` = the API, `searchDisabled` = server-side) | the happy path end to end |
| The **control panel** | every `LuiQueryOptions` knob: `Take`, `Candidates`, `Rank` order, `Priorities`, `DistinctText`, `Similarity`, `TypoTolerance`, `Highlight`, `TrackTimings` — each one a live control |
| The **result panel** | the raw `LuiResult` JSON: hits, `CandidatesConsidered`, `Truncated`, `CorrectedQuery`, `Timings` |
| The **hit panel** | per hit: text, key, weight, priority, similarity, `AtWordStart`, `Highlight` span — so the ranking rules are visible, not asserted |
| The **index panel** | list the indices with their stored definitions (`lui_meta`), and the write side: Upsert, UpdateWeight, UpdatePriority, Remove, Sync, Check, Rebuild, Optimize — i.e. the *whole* public API, not only the read path |

**The API** (minimal API, no Mediator — see D3):

| Endpoint | Purpose |
|---|---|
| `GET /api/indexes` | the indices + their stored definitions |
| `GET /api/suggest/{index}` | the query API — **every** option as a query parameter, mapped 1:1 onto `LuiQueryOptions` |
| `POST /api/suggest/{index}` | the same, as a JSON body (for options that do not fit in a query string) |
| `GET /api/index/{index}/entries` | list entries (to show what the index holds) |
| `POST /api/index/{index}/entries` | Upsert (key, text, weight, priority, payload) |
| `PATCH /api/index/{index}/entries/{key}` | UpdateWeight / UpdatePriority |
| `DELETE /api/index/{index}/entries/{key}` | Remove |
| `POST /api/index/{index}/sync` · `GET /api/index/{index}/check` · `POST /api/index/{index}/rebuild` · `POST /api/index/{index}/optimize` | the maintenance surface (§12.6) |

The sample seeds two indices so both index kinds are visible: `recipes` (**MultiWord**, Owned) and `tags` (**SingleWord**, Linked) — and `tags` carries a **System**-priority entry (`vegan`) so the priority rule (E10) is visible in the UI, not just in a test.

### D8 — Publishing: ready, owner-gated

`dotnet pack` → `Nordmann.LUI.0.1.0.nupkg`. `ci.yml` runs a **package smoke test**: a throwaway console app installs the `.nupkg` from a local feed and executes the README's quick-start code — so a broken README is a red build, not a documentation bug someone finds later.

`publish.yml` triggers on a `v*` tag with the `NUGET_API_KEY` secret. **The owner runs the tag** — publishing is the owner's step (the previous RC1 never got a tag, and nothing was ever on nuget.org; this plan fixes the pipeline, not the key).

### D9 — Sync API (not async) — carried from the spec, and it is the right call

The spec §13 makes the API synchronous because `Microsoft.Data.Sqlite` has no async I/O. That is true and worth keeping: a `Task`-returning API over a synchronous driver is a lie about latency. The sample's ASP.NET Core endpoints therefore call the library **directly in the request** (1–3 ms, CPU-bound), and only the long operations (`Sync`, `Rebuild`) take a `CancellationToken` and run behind a hosted service.

---

## 4. The package's public surface (one assembly)

> **Superseded by [ADR-0013](../adr/0013-the-api-shape-before-the-first-release.md)** (issue #11): every public
> type lives in `Nordmann.LUI` and starts with `Lui`/`ILui`; the other namespaces are internal. The layout below is
> the plan as it was written.

```
Nordmann.LUI                     LuiCatalog, LuiCatalogOptions, LuiCatalogBuilder
Nordmann.LUI.Indexing            LuiIndexBuilder, IndexKind, IndexSource, Ensure/Drop, Sync, Check, Rebuild, Optimize
Nordmann.LUI.Querying            ILuiIndex<TKey,TPayload>, LuiQueryOptions, LuiResult, LuiHit, RankingOrder, PriorityRanking, Priority
Nordmann.LUI.Normalization       Normalizer, NormalizedText (search form + original-position map)
Nordmann.LUI.Typos               TypoDictionary (prefix SymSpell)
Nordmann.LUI.Similarity          ITextSimilarity, Similarity (JaroWinkler | NgramDice | DamerauLevenshtein | Levenshtein)
Nordmann.LUI.Payloads            IPayloadSerializer<T>, LuiRow
Nordmann.LUI.DependencyInjection AddLui(), keyed services, ensure-on-start
```

All of §12's API, unchanged in shape — only the *packaging* is collapsed to one package (D1).

---

## 5. Delivery plan — one PR per issue, one issue per PR

The spec §20 gives nine steps; I add a repo foundation and the sample, and split the "fluent polish" step so the README is not written against an API that still moves. **12 PRs**, each with ADR + tests + measurement.

| PR | Content | Done when |
|---|---|---|
| **001** `chore: repo foundation` | solution, `Directory.Build.props`/`.Packages.props`, `.editorconfig`, CI, `AGENTS.md`, LICENSE, ADR-0001 (one package) · 0002 (library vs. standards) · 0003 (defer O1–O9) · 0004 (dependencies) | build green, arch tests green, `dotnet pack` produces the `.nupkg` |
| **002** `feat: normalizer` | `Normalizer` (C#) + the SQL expressions, §6 tables, **parity test** on the curated corpus | parity holds for the whole curated corpus + the spec's edge cases — **done, see §6b** |
| **003** `feat: single-word owned index` | schema, triggers, packed rowid, candidates, prefix path (1–2 chars), ranking, payload-for-hits-only, `Upsert`/`UpdateWeight`/`UpdatePriority`/`Remove` | golden queries (§Anhang B) green · plan test (`INDEX 64`, no `TEMP B-TREE`) · p50 ≤ 2 ms / p95 ≤ 5 ms on 3M entries |
| **004** `feat: multi-word index` | word split, `s_compact`, word-start on the compact form, N-gram Dice | golden queries for `MultiWord` green |
| **005** `feat: typo tolerance` | prefix SymSpell dictionary, 1 edit, 3 corrections, correction triggers only when *no* candidate starts a word | quality floors (§19) green · concurrency test green |
| **005a** `feat: api shape` | the review of the API before the first release (issue #11): staged builder, read/write interfaces, `LuiEntry` + `UpsertMany`, named results, one namespace, `Lui` prefix, unused dependencies out | ADR-0013 · smoke test runs the README quick start — **inserted after PR-005** |
| **005b** `refactor: seams for linked` | the kind as a strategy, the content source as a strategy, the direction of the namespaces, a smaller `QueryRunner` — no behaviour change | ADR-0014 · schema byte-identical · tests unchanged · measurements unchanged — **inserted before PR-006** |
| **006** `feat: linked index` | `LinkTo`, **loosely coupled** (owner's decision, ADR-0015): no column, index or implicit trigger on the app table; `Sync`, `Refresh`, optional triggers; `Owned<long>` as the builder's default | integration tests against an app table green |
| **007** `feat: sync / check / rebuild / optimize` | `Sync` (batched, resumable), `Check(source)`, `Check()`, `Rebuild()`, `Optimize()` | 1M-entry sync · abort + resume green |
| **008** `feat: DI and query polish` | `AddLui`, keyed services, ensure-on-start, hosted `ApplyPending`, stale candidates past the budget; `Highlight` → later version (#20, owner's decision); `.SuggestWhenEmpty` → *no* (D5) | DI smoke test green |
| **009** `feat: performance budgets` | BenchmarkDotNet project, 3M-corpus generator, §16 targets as tests | every §16 target green or explicitly waived with a reason |
| **010** `docs: readme, changelog, benchmarks` | the README (§7), `docs/benchmarks.md`, `CHANGELOG.md`, package smoke test with the README code | smoke test green on the packed `.nupkg` |
| **011** `feat(sample): the inline static site` | `samples/Lui.Sample` — API + page, seeded corpus, control panel, result panel, index panel | `dotnet run` → the page works; every API knob reachable from the UI |
| **012** `chore: release 0.1.0` | version, changelog, tag preparation (the **owner** pushes the tag) | `dotnet pack` + smoke test green on the release commit |

**PR-002 is deliberately first and deliberately small.** The SQL ↔ C# parity (§6.2) is the highest-risk claim in the spec; proving it on a small corpus in PR-002 turns a build-breaking surprise in PR-003 into a two-day spike.

---

## 6. Acceptance bar

The plan is done when **all** of these hold — and I will report each one with evidence, not assertion:

1. **Test delta**: the §19 stages all exist and are green — unit · SQL↔C# parity · integration · golden queries · quality floors · query plan · concurrency · performance.
2. **Architecture tests** green (NetArchTest: no `Version` on any `PackageReference`; namespace ↔ folder mapping per §17.2; no `System.Text.Json` reflection path; no `Task`-returning public API — §13 is a *test*, not a comment).
3. **`dotnet format --verify-no-changes`** clean; `TreatWarningsAsErrors` on.
4. **`dotnet pack`** → `Nordmann.LUI.0.1.0.nupkg`; the package smoke test installs it from a local feed into a throwaway application and runs it (`tools/package-smoke-test.sh`).
5. **The mutation check** (`tools/mutation-check.sh`) — every architecture guard is mutated in an isolated copy and must react. A guard that has never been seen red is a guess. The script reports *how* each mutation was caught: **CAUGHT BY TEST** or **REFUSED BY BUILD** — the difference matters, because a mutation caught for the wrong reason proves nothing.
6. **The sample runs**: `dotnet run --project samples/Lui.Sample` → the page loads, a suggestion round-trips, and every control changes the JSON result.
7. **Benchmarks**: `docs/benchmarks.md` carries the measured numbers for the §16 targets, with the machine and the corpus described.
8. **Everything on a branch with a PR** — never straight to `main` (the standards' rule).

---

## 6b. PR-002 — what the measurement changed

PR-002 was supposed to write a normaliser. It spent most of its time
**measuring** the tokenizer first, and the measurement moved three decisions:

1. **The C# side cannot use a Unicode rule.** SQLite folds text with its own
   tables, and they are not .NET's: **696 characters** of the Basic
   Multilingual Plane are folded differently, and 680 of them differ from
   "ToLowerInvariant plus strip the diacritic of the precomposed character" —
   the rule one writes from the specification. The digraphs (`ĳ`, `ǳ`) and the
   ligatures (`ﬀ`) are *not* decomposed, the Greek tonos is *not* removed, `İ`
   folds to `i` (unlike .NET) and `ẞ` to `ß`. ⇒ The C# side uses a **measured
   table** (1,263 mappings, 25 removals), generated by
   `tools/FoldTableGenerator` and re-verified against the live tokenizer by
   the parity test on every build.
2. **The transliteration has to come before the folding.** The folding removes
   the umlaut, so `Ä` folded first becomes `a` and the transliteration has
   nothing left to do; transliterated first it becomes `Ae` → `ae` — the same
   form the query produces.
3. **The tokenizer treats the whole text as one token** — the spaces and the
   punctuation stay inside the trigram stream (`E-Mail` → `e-mail`). That is
   what makes the compact column necessary, and it is now documented instead
   of assumed.

The gate that came with it: the parity test is not a sample. It sweeps the
whole Basic Multilingual Plane against the live tokenizer, and the release
gate runs all 685,829 words of the German list.

## 6a. PR-001 — what actually happened

The foundation landed with two gates that the plan did not name, and one
decision the plan left open:

| Addition | Why |
|---|---|
| `tools/package-smoke-test.sh` | The plan said "the README's code is executed by the build". The script that does it now exists: pack → install from a local feed into a throwaway app → run it. The isolation matters — a cached `Nordmann.LUI` in `~/.nuget/packages` can otherwise win over the freshly built one. |
| `tools/mutation-check.sh` | The plan's acceptance bar said "architecture tests green". Green is not evidence. Seven mutations now prove that the guards react. Two of the seven are refused by the *build* (central package management), five by the *test*. |
| `tools/fetch-wordlist.sh` + `tools/make-curated-corpus.sh` | The corpus is fetched, not curated by hand: a deterministic stride over the 685,829-word list. The Leipzig original sits behind an anti-bot wall; the CC0 mirror is what works. |

The plan's §6 grew items 4 and 5 accordingly. The twelve pull requests are
otherwise unchanged.

## 6b. PR-003 — what actually happened

PR-003 carried the part of spec step 1 that PR-001 left out (`LuiCatalog.Open`,
`lui_meta`) and the decision `docs/normalization.md` §7 had deferred to it. It
also changed three things the plan did not foresee:

| What | Why |
|---|---|
| The write path keeps **prepared statements** on a writer connection ([ADR-0009](../adr/0009-writes-use-prepared-statements.md)) | A fresh command per `Upsert` cost 184 µs; a prepared one 59 µs. The §16 build target is out of reach at 184 µs. |
| `LuiCatalog.Open` **probes the folding** of the loaded SQLite ([ADR-0007](../adr/0007-the-catalog-checks-the-sqlite-it-runs-on.md)) | The fold table is measured data; a different SQLite makes an index invisible, silently. |
| **The query budget of §16 is not met at 3 M entries** ([ADR-0010](../adr/0010-the-query-budget-at-three-million-entries.md), *Proposed*) | Measured: p50 3.6 ms, p95 26 ms on a trigram-dense synthetic corpus; the time is FTS5's. The plan's "done when" for step 3 named the budget; the pull request reports the numbers instead of the promise, and asks the owner. |


## 6c. PR-004 — what actually happened

| What | Why |
|---|---|
| `Similarity.NgramDice` is **our own, allocation-free** ([ADR-0011](../adr/0011-the-multi-word-index.md)) | Measured: 3.6 MB per query at the 95th percentile with `F23`'s Dice (a dictionary per comparison). Equal to `SorensenDice(2)` on 10,000 random pairs. |
| One word is searched in `s_compact` alone | Same hits as §8.2's two arms (tested on 1,500 titles), one posting list instead of two. |
| The search index of a multi-word index holds **`s_compact` only** (owner's decision; §7.4 and §8.2 corrected) | −36 % file, −40 % build, −30 % latency on 500 k titles; one more candidate in 190. |
| The file size target of §16 (1 M `MultiWord` ≤ 300 MB) is **missed**: 445 MB | long titles (36 characters), and the table holds the text plus two generated forms. |

## 6d. PR-005 — what actually happened

| What | Why |
|---|---|
| Typo tolerance is **off by default** (`.TypoTolerance(1)` switches it on; a query overrides) ([ADR-0012](../adr/0012-typo-tolerance.md)) | The dictionary costs 138 MB for 685,000 words and a few seconds on first use; an application should decide on that. |
| Frequency = sum of **`weight + 1`** | SymSpell drops a count of 0, and half of a tag stock has the weight 0. |
| The candidates of the input **stay** when a correction is searched; each candidate is ranked against the input it was found with | §10.3 says „unite"; the first reading would have lost the mid-word hits and the similarity of the wrong input. |
| A typo **after the tenth letter** is not corrected; neither is a letter left out or too many in a word of ten letters or more | The dictionary holds the beginnings of 3 … 10 letters (§10.1) — pinned by tests, written down. |
| The p95 of a corrected query is 8.7 ms at 685 k entries (target 10 ms); at 3 M entries it is untested | up to three more FTS statements on a row-by-row index; the bulk build (PR-007) is the lever. |

## 6e. PR-007 — what actually happened

| What | Why |
|---|---|
| The catalog is a facade over `Storage.Database`, `Indexes.IndexStore`, `Typos.TypoDictionaries` (review DevEx D4, D6) | `Sync`, `Check`, `Rebuild` need connections in `Indexes`; no layer knows the catalog any more, the exception of ADR-0014 is gone ([ADR-0016](../adr/0016-sync-check-rebuild.md)) |
| `Sync` crosses off a snapshot of the keys there at its start, instead of marking the keys it sees | removed is exactly what was there and is not in the source; a key the application adds meanwhile stays |
| The bulk path is sorted batches with `automerge` off and one merge — not "content first, then `rebuild`" | measured: the merge decides the query (p95 5.7 → 3.0 ms at 1 M); `rebuild` is not faster than large batches and cannot be run again |
| The default batch stays at 10,000; 100,000 is about 30 % faster for a first load | the number of commits costs more than the order; the default is the owner's call (ADR-0016) |
| No shadow build for `Linked.Sync()` (S2) | measured: one transaction of 26.7 s per million rows; a shadow build needs a second write path for every refresh — the owner decides |

## 6f. PR-008 — what actually happened

| What | Why |
|---|---|
| The DI is a second package, `Nordmann.LUI.Hosting`, released with the core under one version; only it references the DependencyInjection, Hosting and Logging abstractions | owner's decision after a discussion ([ADR-0018](../adr/0018-two-packages-core-and-hosting.md)): a console application carries no host; the core runs no thread of its own |
| Two hosted services: ensure on start (+ optional warm-up), `ApplyPending` on an interval | the warm-up of ADR-0013 and the timer of ADR-0015 |
| `Nordmann.LUI.Hosting` knows only the public API | another assembly, no `InternalsVisibleTo`: the compiler pins it; `ILuiIndex.TypoTolerance` is new for the warm-up |
| An outdated linked candidate does not count against the budget (F1) | many of them among the best crowded out a match; the read goes on up to a second budget |
| `Highlight` is not built | owner's decision: the front end marks the match; issue #20 for a later version — **struck** on 2026-11-04 (ADR-0028) |
| The sample does not use LUI yet | on purpose: the sample comes with PR-011 and will use `Nordmann.LUI.Hosting` there (review of PR #23, N6) |

## 6g. PR-009 — what actually happened

| What | Why |
|---|---|
| A test per target of §16, in its scenario (`PerformanceBudgets`); a target is met and asserted, or waived with a reason | "every §16 target green or explicitly waived with a reason" — four met, four waived, none lowered ([ADR-0020](../adr/0020-the-performance-budgets-measured.md)) |
| One generated corpus (`Corpus`) for the budget tests and the benchmarks | the same entries, inputs and typos for a seed; the 3 M corpus generator of the plan |
| The build of 3 M meets 3 min only with `Sync` and batches of 1,000,000 (172.5 s; 190 s with 100,000) | the number of commits, measured in ADR-0016 |
| The query p95 is the input length: ≤ 3.1 ms up to four characters, 17.5 ms at eight | FTS5's cost at 3 M. The owner stated the budget per input length (ADR-0010, option 2), the correction, the allocation (bug #30) and the file size anew (2026-11-04, ADR-0020) |
| The benchmark project measures the code on 200,000 entries, not the scenario | a benchmark is about the code; the scenario is the budget test's |

---

## 7. README outline (the front door)

| Section | Content |
|---|---|
| Title + one paragraph | what LUI is, in the user's terms: *suggestions for a search field, out of the app's own SQLite database* |
| **What this is / what this is not** | the standards' structure: not a full-text search engine, not a server, not a KV store |
| Install | `dotnet add package Nordmann.LUI` |
| Quick start | the 12-line version: open a catalog → define an index → upsert → query. **This exact code is the smoke test** |
| The two index kinds | `SingleWord` vs `MultiWord`, with the difference in one table |
| Owned vs Linked | the same table as the spec §5, in consumer language |
| The query API | every option of `LuiQueryOptions`, one line each, with the default |
| Ranking & priority | the rules the user *feels* (§9.1 as a table), and why priority comes first (E10) |
| Typo tolerance | when it triggers, what `CorrectedQuery` means |
| Performance | the §16 numbers, measured, with the machine |
| Sample app | how to run it; a screenshot-free description of what it demonstrates |
| License & contributing | MIT, ADR workflow, PR rules |

---

## 8. Risks

| Risk | Severity | Mitigation |
|---|---|---|
| **SQL ↔ C# normalizer parity fails on exotic characters.** The SQL side is a `replace()` chain + the tokenizer's `remove_diacritics`; the C# side does Unicode decomposition. These are *not* the same function for `İ`, `Ł`, `ß`/`ss`, Turkish dotless i, or ligatures. | **High** — it is the spec's own duplication (§6.2), and it fails *silently* (search misses, no error) | **Measured and settled in PR-002**: SQLite folds with its own tables — 697 characters of the BMP differ from `char.ToLowerInvariant`, 681 from the precomposed-diacritic rule. The C# side uses a *measured table*, verified against the live tokenizer on every build (ADR-0005). |
| **The application's SQLite may fold differently from the build's.** The table is measured from the SQLite that `Microsoft.Data.Sqlite` 10.0.14 bundles; an application that loads a system SQLite folds with other tables — and an index stored with the old folding becomes invisible to the query. | **High** — silent search misses in exactly the deployments that bring their own SQLite | **Decided in ADR-0005, implemented in PR-003**: `LuiCatalog.Open` probes the folding of the SQLite it talks to (a few dozen code points) and fails with a clear error on a mismatch; the fold-table version goes into `lui_meta`, so an index stored with an older table is detected and the application is told to `Rebuild()`. |
| A **decomposed umlaut** (`Mu` + U+0308 + `ller`) was transliterated differently from the composed one — two spellings, one of them unfindable. | **High** — silent, and macOS file names and some clipboards deliver that form | **Fixed in PR-002**: both sides transliterate the letter plus the combining diaeresis; the edge cases carry the decomposed forms as assertions. |
| The prefix SymSpell dictionary costs **179 MB / 676k prefixes per index** (§10.1, Anhang A) | Medium | Build it lazily on first typo-tolerant query, not at `Ensure()`; expose `RebuildTypoDictionary()`; document the RAM cost in the README |
| `Linked` mode writes to the **application's schema** (5 columns + 3 indexes + 3 triggers) | Medium | **Resolved by ADR-0015:** `Linked` never changes the application's table; triggers only on an explicit call or through the application's own migration (`TriggerScript()`) |
| `symspell` + `F23.StringSimilarity` are **outside the standards' closed library list** | Low (process) | ADR-0004 names both, with license and alternatives |
| The spec's §16 targets were measured on a 4-core Xeon with the DB in the page cache — **this sandbox is smaller** | Medium | Measure and publish the *sandbox* numbers honestly; keep §16 as the target and report the delta, rather than lowering the target silently |
| `Microsoft.Data.Sqlite` bundles its own SQLite — the consumer's app may load a **different** SQLite version | Medium | The library checks `sqlite_version()` at open and fails loudly below 3.45 (§17.3) instead of misbehaving |

---

## 9. Assumptions (the questionnaire could not be confirmed, so I chose)

Each of these is a decision I made and would happily revisit — they are all cheap to flip:

1. **Sample shape:** one inline `index.html` (inline CSS + inline vanilla ES module) with flexdatalist vendored — *not* Alpine/Tailwind, *not* a module tree. Reason: "inline static website" + "flexdatalist js only version" both point at a self-contained page; the sample demonstrates the library, not a frontend stack.
2. **Open questions O1–O9:** all deferred, one ADR each (D5). Nothing from §21 enters 1.0.
3. **Corpus:** curated in-repo + opt-in full-list script (D6).
4. **Publishing:** pipeline ready, owner-gated on the tag and the API key (D8). Nothing is pushed to nuget.org from my side.
5. **License:** MIT, "Copyright (c) 2026 Nordmann" — same holder as the standards.
6. **Version:** `0.1.0` for the first publishable package (the old RC1 never shipped; this is a different design, not a continuation of it).
7. **Repo is private, PRs go through branches** — I will not push to `main` without an explicit ask.

---

## 10. Next step

**Start PR-001** — the repo foundation: solution, props, CI, `AGENTS.md`, the four founding ADRs, and a `dotnet pack` that produces an empty-but-valid `Nordmann.LUI.0.1.0.nupkg`. It is small, it proves the whole toolchain (SDK, central versions, analyzer, arch tests, packaging) on a tree with no feature code in it, and every later PR builds on it.

Before I touch the remote: **the plan above is uncommitted in `/workspace/repos/LUI/docs/plan/`.** Tell me to commit it (and whether to open it as a PR branch) and I will.

One answer I would still like, because it changes real work: **do you want `symspell` and `F23.StringSimilarity` in the package at all**, or would you rather I implement SymSpell myself (a few hundred lines, no dependency, but a correctness risk I would have to test myself)? My recommendation is to take both dependencies and record the deviation in an ADR — but it is your call, and it is the one place where the standards' closed-list rule and the spec's design genuinely pull apart.
