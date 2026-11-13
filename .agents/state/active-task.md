# Active task

<!--
What the next session is doing. One task, in the words of the issue that
describes it. Link the issue; do not repeat the whole plan here.
-->

**Issue:** #128 — the C# blocks of the tutorials are compiled by a guard. It is the fourth pull request of the round
after the release.
**Branch:** `feature/128-the-tutorial-blocks-are-compiled` carries the fourth pull request of the round; `dev` is at
`84bcf45`, `main` at `e316c4b` — the tag `v0.1.0` is on the commit of `main`.
**Decision:** the plan
([the round after the first release](./../plan/2026-11-13-the-round-after-the-first-release.md)) — D1 to D7, on the
owner's behalf, 2026-11-13.

## What is done

- **LUI 0.1.0 is released** (2026-11-13): the tag `v0.1.0` on `main`, the packages `Nordmann.LUI` and
  `Nordmann.LUI.Hosting` at the version `0.1.0`, and the freeze of
  [ADR-0026](../adr/0026-the-public-api-from-0-1-0-on-grows-only-additively.md) in force — the public API grows only
  additively from this version on.
- **The whole series is merged**: the public API of the review of 2026-11-04 (ADR-0063), the route named in the call
  (ADR-0062), the chapter that says why the tests are built this way (#81), the warm-up that leaves nothing running
  (#148), and the repository English throughout (ADR-0057).
- **The epics #39, #64 and #72 are closed** with the release.
- **The plan of the round is written**: `0.1.1` carries the six dead paths of the records and the three missing guards;
  `0.2.0` carries the measured work and the decisions.
- **The six dead paths of the records are repaired**, and a guard holds them up: every path of the list of the affected
  files of a current record names a file that exists.
- **The three missing guards of the round are there**: the paths of the records (#137), the index of the records (#129), the
  members of the API page (#130) and the C# blocks of the tutorial (#128).

## Open

The four pull requests of the first round, in the order of the plan:

1. ~~**#137**~~ — the dead paths in the records: six paths in three records. **Done.**
2. ~~**#129**~~ — the index of the ADRs gets a guard. **Done.**
3. ~~**#130**~~ — the API page guards the members, not only the types. **Done.**
4. ~~**#128**~~ — the code blocks of the tutorials are compiled by a guard. The issue names the tutorials by their old
   names; they are `01-first-index.md` … `05-in-the-application.md` since PR #135. **Done.**

Then the release of `0.1.1`, and the second round: **#134** (the 1912 bytes), **#32** (the first load of 3 M entries),
**#31** (the shadow build of `Linked.Sync()`), **#44** (the dependency on `symspell`) — and **#154**, the test of the
integration layer that failed once under load and did not reproduce.

## The next step

**The second round** — the measured work and the decisions, on the plan of the round
([the round after the first release](./../plan/2026-11-13-the-round-after-the-first-release.md)):

1. **#32** — a faster first load (the build of 3 M entries).
2. **#31** — a shadow build for `Linked.Sync()`.
3. **#44** — the dependency on `symspell`: keep it or implement SymSpell ourselves (an ADR, the dependency set is the
   contract of the package).
4. **#134** — the origin of the 1912 bytes inside the measured window.
5. **#154** — the test of the integration layer that failed once under load and did not reproduce.

They belong to `0.2.0` (D2), and they need a plan of their own before the first pull request — the round after the first
release was planned and written before its first pull request, and so is this one.
