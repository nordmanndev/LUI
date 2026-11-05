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
**State:** implemented, verified and pushed; the owner's reviews of 2026-11-05 are answered in the same branch (the second
round: short settings, the word list downloaded by the sample, the indexes created on demand, a fill of up to three million
entries; the third round: a tab per index, the settings in their own column, and a page where one thing that breaks does not
take the rest down). The owner decides about the merge; the release (PR-012) is the step after it.

## Decided by the owner (2026-11-05, second round — the review of the pull request)

- **The settings are short**: every setting explains itself behind a "?" (the explanation is the tooltip as well), the panel
  fits a screen without a scroll of its own, stands beside the autocomplete and scrolls with the page. Nothing is cut off, and
  there is no trap to scroll out of.
- **The word list is the full one, and the sample downloads it** — 685,829 words (CC0) from the same source and into the same
  place as `tools/fetch-wordlist.sh`, when it is not on the machine. The curated 7,071-word subset is what the *tests* use: the
  tests point the sample at it, and a test never downloads 5.5 MB.
- **The bulk indexes are created on demand** — they are not registered with the host; the index of LUI is created when the panel
  asks for it or when the button is pressed. A stock of a million entries is minutes of work, and nobody pays for it who does
  not want it.
- **The scale**: the largest fill is three million entries (the scale of the measurements of the specification, §16) — the fill
  of 685,829 words took 2 m 15 s, a million names about the same.

## Decided by the owner (2026-11-05)

- The three bulk indexes are **new** indexes beside the curated ones: `words` (the German corpus), `people` (the names of
  Bogus's German locale), `hashtags` (a place and a surname of the locale, joined by a hyphen). The curated `recipes`, `names`
  and `tags` keep their entries, their payload and their purpose — they show the API surface, the bulk indexes show the scale.
- The button does init and load: the index is created when it does not exist, and it is loaded with exactly the number of
  entries that was asked for. A fill with fewer entries removes the rest; the source of a bulk index is a function of the
  number, so a fill with more adds only what is missing.
- The fill runs in the background of the sample (`Task.Run`), and the page polls its progress — a fill of a million entries takes
  minutes, and a request that waited for it would time out.
- The word list is looked up in `Sample:WordList` (the configuration of the host), then in `LUI_WORDLIST`, then in
  `artifacts/wordlist/german-words.txt` (what `tools/fetch-wordlist.sh` writes) — and the sample downloads it when none of
  them is there. The index panel says which list it found and how many words it has — the full list is not checked into the
  repository, and the sample must not pretend it is.
- Bogus is a dependency of the **sample alone** (the sample is not shipped); the two packages keep their dependency set
  (ADR-0004, ADR-0018). That is why ADR-0029 exists: `Directory.Packages.props` changed.
- The typo tolerance is on for `people` only: a dictionary of 685,829 words costs about 140 MB (ADR-0021).

## The next step

**Next:** the owner reads the diff and decides about the pull request; the release (PR-012) is the step after it. After that:
#35, #30 (with the own Jaro-Winkler), #31, #32, #38, #44, the `compactLevel` follow-up of #48.
