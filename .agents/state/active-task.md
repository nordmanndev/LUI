# Active task

<!--
What the next session is doing. One task, in the words of the issue that
describes it. Link the issue; do not repeat the whole plan here.
-->

**Issue:** [#54](https://github.com/nordmanndev/LUI/issues/54) — the owner's request of 2026-11-05: "the sample app needs to have way
more data to show the motivation behind LUI — create one index based on german words corpus, one for names based on Bogus and tags
also based on bogus. Integrate the creation of the index (init & load) as a button in the sample app, so the user can start and
'fill' the index he is willing to play with. Maybe with num of entries where appropriate."
**Branch:** `feature/sample-bulk-indexes` → the pull request is [#55](https://github.com/nordmanndev/LUI/pull/55), open against
`main`.
**State:** implemented, verified and pushed. The owner decides about the merge; the release (PR-012) is the step after it.

## Decided by the owner (2026-11-05)

- The three bulk indexes are **new** indexes beside the curated ones: `words` (the German corpus), `people` (the names of
  Bogus's German locale), `hashtags` (a place and a surname of the locale, joined by a hyphen). The curated `recipes`, `names`
  and `tags` keep their entries, their payload and their purpose — they show the API surface, the bulk indexes show the scale.
- The button does init and load: the index is created when it does not exist, and it is loaded with exactly the number of
  entries that was asked for. A fill with fewer entries removes the rest; the source of a bulk index is a function of the
  number, so a fill with more adds only what is missing.
- The fill runs in the background of the sample (`Task.Run`), and the page polls its progress — a fill of a million entries takes
  minutes, and a request that waited for it would time out.
- The word list is looked up in `LUI_WORDLIST`, then in `artifacts/wordlist/german-words.txt` (what
  `tools/fetch-wordlist.sh` writes), then in the curated subset of the tests. The index panel says which one it found and how
  many words it has — the full list is not checked into the repository, and the sample must not pretend it is.
- Bogus is a dependency of the **sample alone** (the sample is not shipped); the two packages keep their dependency set
  (ADR-0004, ADR-0018). That is why ADR-0029 exists: `Directory.Packages.props` changed.
- The typo tolerance is on for `people` only: a dictionary of 685,829 words costs about 140 MB (ADR-0021).

## The next step

**Next:** the owner reads the diff and decides about the pull request; the release (PR-012) is the step after it. After that:
#35, #30 (with the own Jaro-Winkler), #31, #32, #38, #44, the `compactLevel` follow-up of #48.
