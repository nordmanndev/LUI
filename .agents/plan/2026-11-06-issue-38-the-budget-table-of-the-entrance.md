# The plan of the budget table of the entrance

> The budget table of the README is copied by hand, and it had drifted from the contract. This plan is what the round
> does about it. The decision is in ADR-0040.

**Issue:** #38.
**Branch:** `feature/budget-table-guard`, from `main` at `dd75649`.

## The steps

1. **The guard.** A new architecture test, `Readme_Budgets`: it reads the target table of ADR-0020 out of the
   specification and compares the limits with the budget table of the README and with the target table of the record —
   in order, so a swapped limit is caught as well as a changed one.
2. **The drift it finds.** The allocation row promised ≤ 250 / ≤ 350 KB; the specification and the budget test have
   held ≤ 60 / ≤ 100 KB since ADR-0032. Both tables are corrected, and the measured values are the numbers of the run
   of 2026-11-06 (46 / 51 KB — bytes, so the machine does not matter).
3. **The teeth.** A mutation in `tools/mutation-check.sh` changes one limit of the README; the guard has to fail.
4. **The paper.** ADR-0040, the CHANGELOG, the test map, the note of the session.

## The assumptions

1. **The specification is the contract** — the limits are read from it, so the build depends on it. When the owner
   deletes the specification at the end of the release, the limits have to live somewhere else; where is the owner's
   decision.
2. **The measured values are not checked.** They belong to a machine and a day; a test that knows them would have to
   hard-code them.
3. **The public API does not change.**
4. **The merge is the owner's.**

**Verified by:** `Readme_Budgets` (two tests), the mutation `a-budget-limit-in-the-readme-edited-by-hand`, the whole
test suite, and the link check.
