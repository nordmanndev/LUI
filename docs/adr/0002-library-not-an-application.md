# 0002 — LUI is a library, not an application: the standards delta

- **Status:** Accepted
- **Date:** 2026-11-02
- **Supersedes:** —
- **Affects:** `AGENTS.md`, `docs/architecture/alignment.md`, `Directory.Build.props`,
  `samples/Lui.Sample/`

## Context

[`nordmanndev/standards`](https://github.com/nordmanndev/standards) defines how
every Nordmann **project** is built: DDD + CQS + multi-tenancy + SQLite,
Mediator dispatch, OrmLite persistence, an outbox, Serilog → Seq, and an
Alpine.js + Tailwind frontend.

LUI is none of those things. It is a **NuGet library**: no aggregates, no
handlers, no tenants, no process of its own, no browser code. Applying the
standards literally would produce a worse library — a Mediator source
generator in every consumer's build, an ORM wrapped around the raw SQL the
whole design depends on, an ID type imposed on applications that already have
their own.

The standards' own rule is explicit about this case: *"Ask before: branching
off the standard for a single project."* This ADR is that answer, written down.

## Decision

**The standards apply where they describe *engineering hygiene*, and not where
they describe *application shape*.**

### Applies — and is enforced mechanically

| Rule | Where it lands |
|---|---|
| `Directory.Build.props` (net10.0, nullable, warnings-as-errors, analyzers) | verbatim from `architecture/layering.md` |
| Central package management | `Directory.Packages.props`; no `Version` on any `PackageReference` — architecture test |
| `Meziantou.Analyzer` from day one | standards entry 9 — "adopted from day one" |
| Test pyramid | xUnit + Shouldly + Alba + NetArchTest — unit, architecture, integration |
| ADR discipline | `docs/adr/`, one ADR per decision, in the same pull request |
| `CHANGELOG.md` on public API change | unchanged |
| Closed-library policy | every dependency outside the standards' catalogue gets an ADR (see ADR-0004) |
| README shape | "what this is / what this is not / install / quick start" — the standards' own structure |

### Does not apply — with the reason

| Standards rule | Why not here |
|---|---|
| DDD aggregates, CQS, Mediator | LUI has no domain and no handlers. `LuiCatalog → ILuiIndex → Query` is a library API, not a command bus. A source generator in every consumer's build for no behaviour is a tax, not a discipline. |
| OrmLite persistence layer | LUI *is* the persistence layer, over raw `Microsoft.Data.Sqlite`. The specification's §8 is explicit SQL; a second ORM would hide exactly what the design depends on. |
| Outbox, background worker, integration events | LUI has no events. Its one long operation (`Sync`) takes an `IProgress<T>` and a `CancellationToken` — the shape of a background job, not a queue. |
| ULID identifiers | The specification fixes `TKey` as `long` or `string` (§12.3) — the *application's* key. A library must not impose an ID type on its consumer. |
| Serilog → Seq | A library must not force a logging stack on consumers. LUI takes an optional `ILogger` and nothing else. |
| Multi-tenant by default | LUI opens *one* database per catalog. Tenancy is the application's business; `Linked` mode exists precisely so an app keeps its data where it lives. |
| Alpine.js + Tailwind frontend baseline | That baseline governs *project* frontends. The sample is a **library demo**: its subject is the LUI API. The sample ships one inline page with plain ES modules and its own CSS — no framework, no bundler, no runtime dependency beyond the vendored `flexdatalist`. |

## Consequences

- A reader who knows the standards will notice the missing Mediator and
  OrmLite. This ADR is the answer they will find; `docs/architecture/alignment.md`
  is the long form.
- The sample is deliberately **not** a model of the standards' frontend stack.
  It is a model of **LUI's API**. When a real project consumes LUI, that
  project's frontend follows the standards — flexdatalist is already the
  standards' named autocomplete library.
- Every rule that *is* mechanical here has a test. A rule that is only written
  down is a rule that gets broken by accident.

**Verified by:** `tests/Nordmann.LUI.Architecture.Tests/Public_API_Shape.cs`
and `Repository_Layout.cs`.
