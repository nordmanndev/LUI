# Active task

<!--
What the next session is doing. One task, in the words of the issue that
describes it. Link the issue; do not repeat the whole plan here.
-->

**Issue:** the round of **0.4.0** — the epic [#258](https://github.com/nordmanndev/LUI/issues/258) and its issues
#259–#263: the freeze comes with 1.0.0 (ADR-0072), the types of the application and their format (ADR-0073), the typed
world mirrors the untyped one (ADR-0074), the typed hit carries the payload (ADR-0075), and the sample and the papers.
**Nothing else comes into 0.4.0** (the owner, 2026-11-20): the client series (#212) is not in it, and the alignment with
the Nordmann standards (#221) follows the round in the same milestone.
**Branch:** `dev` — every pull request of the round branches from `dev` and targets `dev`, one per issue, in the order of
the plan. The release is the pull request `dev` → `main`.
**Decision:** the plan ([the round of 0.4.0](./../plan/2026-11-20-the-round-of-0-4-0.md)) and the epic #258 — every
decision of the round was taken by the owner on 2026-11-20.
**Status:** **the round is open.** The plan is in a pull request against `dev`; the owner merges it, and that is the go.
**The next step is #259** (ADR-0072): a branch from `dev`, a pull request against `dev`. Before it, read the index of the
records: the numbers 0072–0075 have to be free (ADR-0071 belongs to the record of the client).

## What is done

- **LUI 0.1.0, 0.1.1, 0.2.0 and 0.3.0 are released** (2026-11-13, 2026-11-13, 2026-11-15, 2026-11-18): the tags on
  `main`, the packages `Nordmann.LUI` and `Nordmann.LUI.Hosting` on nuget.org. The public API did not move in 0.1.1, 0.2.0
  and 0.3.0 — the freeze of
  [ADR-0026](../adr/0026-the-public-api-from-0-1-0-on-grows-only-additively.md) held, and there is no migration note.
- **The round of 0.3.0 is merged and released** (the epic
  [#166](https://github.com/nordmanndev/LUI/issues/166), the issues #169–#183, the records ADR-0066–ADR-0070): the
  catalogue owns the connection, the read path opens read-only, three ways to open a catalogue, and the audit of
  2026-11-13/14 is fixed. The tag `v0.3.0` stands on `main` (`1b34972`).
- **The audit of `main` of 2026-11-20 is green**: 134 architecture tests, no warning, no forbidden package, no circular
  dependency. The verdict is PASS; the findings of the audit of 2026-11-13/14 are fixed.
- **The standards are read**: `architecture/library/public-api.md` (the API baseline), the agent contract, the workflow of
  the pull requests, and the skill of the milestone. The alignment with them is the issue #221 — after this round.
- **The round of 0.4.0 is planned** (2026-11-20): the goal, the rules, the five pull requests, the acceptance. The three
  decisions of the owner are in the epic #258 and in the issues #259–#263.

## Open

- **#259** — the freeze comes with 1.0.0 (ADR-0072). The rule of `AGENTS.md` changes with it.
- **#260** — the register of the payload types (ADR-0073). `LuiCatalogOptions.PayloadSerializer` becomes
  `LuiCatalogOptions.Payloads`.
- **#261** — the typed world mirrors the untyped one (ADR-0074). The renames, `LuiEntry<T>`, `LuiEmbeddedIndex<T>`.
- **#262** — the typed hit carries the payload (ADR-0075). `hit.Payload`, `LuiPayloadMode`.
- **#263** — the sample and the papers.
- **#221** — the alignment with the Nordmann standards: after the round, in the same milestone.

## The next step

**#259** — a branch from `dev`, a pull request against `dev`, red first: the record ADR-0072, the status of ADR-0069, the
rule in `AGENTS.md`, the note in the CHANGELOG.
