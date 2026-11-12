# 0002 — LUI is a library, not an application — and it stands on the rules of this repository

> **Note (ADR-0027, 2026-11-04):** the sample's page is no longer "one inline page": it is plain ES modules in folders by
> concern, with the vendored `flexdatalist` as its only dependency, one fetch boundary and a strict policy. The text below is history.

> **Contract note (ADR-0056, 2026-11-12):** this record was written against a contract outside the repository — a
> private one, that no reader of LUI can know and does not have to. The repository states its own rules since: the
> rules that contract carried for LUI stand in [`AGENTS.md`](../../AGENTS.md) and in the documents of
> [`docs/`](../../docs/README.md), and the file of the budgets (ADR-0056) is the one place a limit is written down.
> The text below is history and stays as it was written.
> **Note (ADR-0057, 2026-11-12, issue #121):** this record was written against an external contract that a reader of a
> public library cannot know. Its decision is restated in the terms of this repository: LUI is a library, not an
> application, and it stands on the rules of this repository — `AGENTS.md` and the documents of `docs/`. The two tables
> below keep every rule and every reason of the record; only the source of the rules changed. The text below is
> history.

- **Status:** Accepted
- **Date:** 2026-11-02
- **Supersedes:** —
- **Affects:** `AGENTS.md`, `docs/architecture/alignment.md`, `Directory.Build.props`,
  `samples/Lui.Sample/`

## Context

LUI is a **NuGet library**: no aggregates, no handlers, no tenants, no process of its own, no browser code.

The rules this repository was born in were written for **applications**: a domain with aggregates, a command bus
between them, an ORM above the database, an outbox, a logging stack, a frontend baseline. They describe the shape
of an application, not of a library. Applying them literally would produce a worse library — a mediator source
generator in every consumer's build, an ORM wrapped around the raw SQL the whole design depends on, an ID type
imposed on applications that already have their own.

The rule of this repository is explicit about this case: a decision that deviates from the rules is **written
down**, in an ADR, in the same pull request as the change (AGENTS.md). This ADR is that answer, written down — it
names the deviations of a library, and ADR-0004 names the second one.

## Decision

**The rules of this repository apply where they describe *engineering hygiene*, and not where they describe
*application shape*.**

### Applies — and is enforced mechanically

| Rule | Where it lands |
|---|---|
| `Directory.Build.props` (net10.0, nullable, warnings-as-errors, analyzers) | the block of the root file, as `AGENTS.md` asks for |
| Central package management | `Directory.Packages.props`; no `Version` on any `PackageReference` — architecture test |
| `Meziantou.Analyzer` from day one | `AGENTS.md` — the analyzers are on from the first commit |
| Test pyramid | xUnit + Shouldly + Alba + NetArchTest — unit, architecture, integration |
| ADR discipline | `.agents/adr/`, one ADR per decision, in the same pull request |
| `CHANGELOG.md` on public API change | unchanged |
| Closed-library policy | every dependency outside the catalogue of this repository gets an ADR (see ADR-0004) |
| README shape | "what this is / what this is not / install / quick start" — the shape `AGENTS.md` asks for |

### Does not apply — with the reason

| What an application does | Why LUI does not do it |
|---|---|
| Domain aggregates, commands, a mediator between them | LUI has no domain and no handlers. `LuiCatalog → ILuiIndex → Query` is a library API, not a command bus. A source generator in every consumer's build for no behaviour is a tax, not a discipline. |
| A persistence layer above the database | LUI *is* the persistence layer, over raw `Microsoft.Data.Sqlite`. The specification's §8 is explicit SQL; a second ORM would hide exactly what the design depends on. |
| Events, an outbox, a background worker | LUI has no events. Its one long operation (`Sync`) takes an `IProgress<T>` and a `CancellationToken` — the shape of a background job, not a queue. |
| Its own identifier type | The specification fixes `TKey` as `long` or `string` (§12.3) — the *application's* key. A library must not impose an ID type on its consumer. |
| A logging stack of its own | A library must not force a logging stack on consumers. LUI takes an optional `ILogger` and nothing else. |
| Multi-tenancy by default | LUI opens *one* database per catalog. Tenancy is the application's business; `Linked` mode exists precisely so an app keeps its data where it lives. |
| A frontend baseline | That baseline governs *project* frontends. The sample is a **library demo**: its subject is the LUI API. The sample ships one inline page with plain ES modules and its own CSS — no framework, no bundler, no runtime dependency beyond the vendored `flexdatalist`. |

## Consequences

- A reader who comes from a house where an application is built will notice the missing mediator and the missing
  ORM. This ADR is the answer they will find; `docs/architecture/alignment.md` is the long form.
- The sample is deliberately **not** a model of an application's frontend stack.
  It is a model of **LUI's API**. When a real project consumes LUI, that project's frontend is its own business —
  flexdatalist is a good choice for an autocomplete, and the sample shows how it is used.
- Every rule that *is* mechanical here has a test. A rule that is only written
  down is a rule that gets broken by accident.

**Verified by:** `tests/Nordmann.LUI.Architecture.Tests/Public_API_Shape.cs`
and `Repository_Layout.cs`.
