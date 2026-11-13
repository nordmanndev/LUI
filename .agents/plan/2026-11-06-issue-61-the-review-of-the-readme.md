# The plan of the review of the README of the release

> The owner read the README of the release (the pull request #62) and named seven gaps. This plan is what the round
> did about them. It builds on the plan of the README itself
> ([2026-11-06-issue-61-the-readme-for-developers.md](./2026-11-06-issue-61-the-readme-for-developers.md)); the
> decisions are in the ADRs.

**Issue:** #61 (the same pull request — the work of the README is not finished).
**Branch:** `docs/readme-and-tutorial`.

## The seven gaps and what was done

| | The gap | What was done | The decision |
|---|---|---|---|
| 1 | **no example of loading many records** | a section "Loading many entries" in the README: `Sync` with a `BatchSize`, the report it returns, the difference to `UpsertMany`, the stream, the linked index. A C# block that the package smoke test executes, with the output it prints. | ADR-0039 |
| 2 | **the complete public API is nowhere documented** | `docs/api.md`: every public type of the two packages — 38 in the core, one in the hosting — with a sentence for each. A new test, `Public_API_Documentation`, fails when a type is missing on the page or when the page (or the README) names a type the code does not have. | ADR-0038 |
| 3 | **"embedded vs. linked" is unclear** | a decision guide of three questions with an answer, before the table that compares the two. | ADR-0039 |
| 4 | **the combinations of `SingleWord` / `MultiWord` are not readable off** | a matrix of the kinds with the typo tolerance on and off — the four combinations — and the properties of the kinds beside it. Every example was run against the library before it was written down. | ADR-0039 |
| 5 | **no film of the sample** | `docs/images/sample.gif`: the autocompletes, the correction of a typo, the fill of a bulk index on demand. | ADR-0039 |
| 6 | **the BenchmarkDotNet results are from 2026-11-05 and are stale** | a fresh run on a quiet machine; the tables are rendered into the README, where the guard holds them against the committed reports. | ADR-0039, ADR-0022 |
| 7 | **the official documentation talks about the internal papers** | the ADRs, the delivery plans and the specification move to `.agents/`, with an index of their own; neither the README nor `docs/` links them any more. | ADR-0037 |

## The steps

1. The measurement: `tools/benchmarks.sh` on a quiet machine — about forty minutes — and the tables into the README.
2. The documentation: the README, the API page, the map, the tutorial, the link check.
3. The papers: the move, every link that follows, the ADRs, the CHANGELOG, the contract.
4. The checks: the build, the tests, the pack, the client tests, the link check, the blocks of the README.
5. The pull request: one commit, no force push, the body with the real output.

## The assumptions

1. The seven gaps are seven gaps of **#61** — the work is not finished, and it stays one pull request.
2. The **specification stays** in the repository until the owner deletes it; it is a paper of the work and moves with
   the others.
3. The **budgets of ADR-0020** stay as they are (they are measured by the integration tests on the CI machine); the tables
   of the BenchmarkDotNet run are the ones that were stale.
4. The **public API does not change** — nothing of this round touches a type, a member or a signature.
5. The **merge is the owner's**.

**Verified by:** `dotnet test` (the unit, the architecture and the integration layer), `dotnet pack`, `npm test` in the
sample, `tools/link-check.py`, the package smoke test (every C# block of the README), the tutorial blocks (compiled and
run), and `Readme_Benchmarks` (the tables against the committed reports).
