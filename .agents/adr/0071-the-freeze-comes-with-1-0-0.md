# 0071 — The freeze comes with 1.0.0

- **Status:** Accepted (owner, 2026-11-20)
- **Date:** 2026-11-20
- **Supersedes:** §4 of [0069](./0069-the-freeze-of-0-1-0-is-lifted-for-0-3-0.md) (the freeze holds again from 0.3.0 on)
- **Builds on:** [0069](./0069-the-freeze-of-0-1-0-is-lifted-for-0-3-0.md) (the freeze lifted for 0.3.0, and its §2: the freeze is not
  the absence of users), [0026](./0026-the-public-api-from-0-1-0-on-grows-only-additively.md) (the freeze of 0.1.0)
- **Affects:**
  - `.agents/adr/0069-the-freeze-of-0-1-0-is-lifted-for-0-3-0.md` — the status names the record that supersedes §4
  - `.agents/adr/README.md` — the index
  - `AGENTS.md` — the rule on breaking the public API, and the identity of the agent
  - `CHANGELOG.md` — the entry under `## [Unreleased]`
- **Issue:** [#259](https://github.com/nordmanndev/LUI/issues/259), part of the epic of 0.4.0 ([#258](https://github.com/nordmanndev/LUI/issues/258))

## Context

[0069](./0069-the-freeze-of-0-1-0-is-lifted-for-0-3-0.md) lifted the freeze of 0.1.0 for 0.3.0, in named places, and said it
holds again from 0.3.0 on (§4): the public API grows and never shrinks, and a break needs a new minor version, a migration
note and a record of its own.

The work of 0.4.0 changes the API in many places at once (typed handles, key types, payloads, the maintenance by name).
Lifting the freeze once per release, place by place, would only repeat the argument of 0069 every time.

## Decision

**The freeze of the public API begins with 1.0.0.**

1. **The reason is semantic versioning, not the number of users.** A `0.x` version promises no stable API, and LUI uses that
   room on purpose. 0069 §2 already says the freeze was not about the absence of users; this record keeps that line and
   only moves the point where the freeze begins.
2. **Until 1.0.0 the public API may change in any part** — even fundamentally. That is **not the goal**: every break needs
   a reason that makes the API clearer, and its record says what the reason was.
3. **A break is** a removed or renamed member, a new optional parameter on an existing method (it is a new overload), a new
   abstract member of an interface (it is a default member, or a break), a renamed or renumbered enum value and a new
   positional parameter of a record. **Every breaking change needs a new minor version, a migration note in `CHANGELOG.md` and a record of its own.**
4. **No `[Obsolete]` bridges before 1.0.0.** A `0.x` minor version removes or replaces a member directly; the migration note
   is the bridge. Deprecation periods start with 1.0.0.
5. **§4 of 0069 is superseded.** The rest of 0069 still applies. The status of 0069 reads "§4 superseded by 0071".
6. **From 1.0.0 on** the API grows and never shrinks, and a break (as defined in 3) needs a major version — the rule of
   [0026](./0026-the-public-api-from-0-1-0-on-grows-only-additively.md), taken up again by this record.

## Consequences

- `AGENTS.md` states the new rule, and the commit identity of the agent: `Claude <noreply@anthropic.com>`.
- `CHANGELOG.md` says under `## [Unreleased]` that the API may change until 1.0.0.
- The releases between 0.3.0 and 1.0.0 may break the API, each with its migration note and its own record; no member is
  marked `[Obsolete]` meanwhile.
- A guard that holds the shape of the API against an earlier release (`ReleaseThreeTests`) is moved by the PR that breaks,
  together with its record.
