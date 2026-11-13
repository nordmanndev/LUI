# 0029 — The sample fills its own bulk indexes on demand

> **Note (ADR-0061, 2026-11-13, issue #125):** the specification this record was written against is deleted.
> Every section of it has a home — the ADR or the page that carries the same rule; the mapping is in
> [ADR-0061](./0061-the-specification-is-deleted-and-every-section-of-it-has-a-home.md). The sentences below
> that name the specification are history; the rule they carry lives in the ADR named beside them.

- **Status:** Accepted
- **Date:** 2026-11-05
- **Requested by:** the owner, 2026-11-05: "the sample app needs to have way more data to show the motivation behind LUI — create
  one index based on german words corpus, one for names based on Bogus and tags also based on bogus. Integrate the creation of
  the index (init & load) as a button in the sample app, so the user can start and 'fill' the index he is willing to play with.
  Maybe with num of entries where appropriate."
- **Revised by:** the owner, 2026-11-05, on the review of the pull request: the settings must be short (the explanation behind
  a "?"), the sample must download the word list itself, and the indexes must be created on demand — "that is why they should
  create the index on demand" — so that the page can show the performance of a million entries.
- **Amends:** [0027](./0027-the-sample-flexdatalist-modular-client-one-fetch-boundary.md) (the sample grows three indexes and a
  button); the dependency set of the *sample* — the two packages keep theirs ([0004](./0004-dependencies.md),
  [0018](./0018-two-packages-core-and-hosting.md))
- **Affects:** `samples/Lui.Sample/**`, `Directory.Packages.props`, `docs/testing.md`, `CHANGELOG.md`, `README.md`

## Context

The sample shows **the API surface** of LUI: three indexes of a few dozen entries each — recipes (embedded, JSON payload),
tags (linked to the application's table, column payload, priority System) and names (embedded, typo tolerance). That is the
demonstration of *how* an application uses the library. It is not the demonstration of *why* the library exists.

The why is the scale: LUI is built for the autocomplete over **very large stocks of short texts** — a catalogue of 685,829
German words, a million names, a hundred thousand tags. The numbers of the library are measured against that: three million
entries build in 172.5 s (ADR-0016, ADR-0020), a query stays in the low milliseconds, and a typo dictionary of 685,000 words
costs about 140 MB (ADR-0021). A sample that holds 25 recipes shows none of it.

The scale demo cannot live in the startup path:

- a fill of 100,000 entries takes about a minute, one of 685,829 several minutes (ADR-0020: 3,000,000 entries in 172.5 s);
- the typo dictionary of such an index costs a hundred megabytes and is built on the first input that needs it;
- the file grows by hundreds of megabytes, and the sample's database is a file in the working directory that nobody wants to
  carry around.

So the scale belongs **behind a button**: the user asks for an index, says how many entries it should hold, and watches it fill.
Nobody pays for it who does not want it.

Three sources are at hand:

- **the German word list.** `tools/fetch-wordlist.sh` downloads 685,829 words (CC0, `enz/german-wordlist`) into
  `artifacts/wordlist/german-words.txt` — deliberately *not* checked into the repository (5.5 MB). The repository tracks a
  curated 7,071-word subset of it for the tests (`tests/TestData/wordlist/german-words.sample.txt`), and the
  convention `LUI_WORDLIST` already names the place of the full list.
- **Bogus** (`bchavez/Bogus`), the .NET library of fake data. Its German locale holds 1,688 surnames, 1,145 first names, 1,031
  street names, 237 countries and 15,482 distinct place names in 20,000 draws; it is deterministic with a fixed seed, and
  `new Randomizer(seed)` is per instance (the static `Randomizer.Seed` is global and must not be used).
- **the curated entries of the sample** — they stay as they are. They are the demonstration of the API surface, and the tests
  of the sample assert on them ("Megan Müller", "vegan", "Schneider Lena").

The question of *where* the bulk indexes live is therefore not "in the curated indexes" but "next to them": an index that holds
half a million words has nothing to do with the demonstration of a payload column.

## Decision

1. **Three bulk indexes next to the curated ones**, filled on demand and named apart from them:

   | Index | Kind | Source | How many |
   |---|---|---|---|
   | `words` | `SingleWord` | the German word list — one word per entry | what the list holds (7,071 or 685,829) |
   | `people` | `MultiWord` | Bogus (`de`): the full names | what the user asks for |
   | `hashtags` | `SingleWord` | Bogus (`de`): a place and a surname, joined by a hyphen (`#Cedrikdorf-Gehrke`) | what the user asks for |

   The curated `recipes`, `names` and `tags` keep their entries, their payload and their purpose.

2. **The button creates the index and loads it, and the index is an ordinary one.** The three bulk indexes are *not* registered
   with the host: the index of LUI is created when it is asked for the first time — when the panel describes it or when the button
   is pressed — and the fill loads it with exactly the number of entries that were asked for. A stock of a million entries is
   minutes of work (ADR-0020: 3,000,000 entries in 172.5 s), and nobody pays for it who does not want it; that is the whole
   reason the scale demo cannot live in the startup path. What the index can do afterwards is what every index can do — the query,
   the check, the maintenance — there is nothing special about it but the way it is loaded.

3. **The fill runs in the background, and the page watches it.** The endpoint answers at once (202) with the state of the fill;
   `GET /api/indexes/{name}/fill` reports what the sync has done so far (`LuiSyncOptions.Progress` tells the report after every
   batch); `POST /api/indexes/{name}/fill/cancel` stops it. A fill of a million entries takes minutes — a request that waits for
   it would time out, and a page that waits for it would say nothing.

4. **The source of a bulk index is a function of the count** — `Entries(count)` — and it is *prefix-stable*: the first entry of
   `Entries(1,000)` is the first entry of `Entries(100,000)`, and its text and weight never change. Therefore a sync against it
   is idempotent, a fill with fewer entries than before removes the rest, and a fill with more adds only what is missing. The
   keys are the positions (1 … count), the weights are a stable hash of the text — so an entry that is already there is *the same*
   entry and not a change.

5. **The word list is the full list, and the sample downloads it.** The list is looked up in this order:
   `Sample:WordList` (the configuration of the host — how the tests point the sample at the curated 7,071-word subset), then
   `LUI_WORDLIST` (the convention of the tests), then `artifacts/wordlist/german-words.txt` (what `tools/fetch-wordlist.sh`
   writes). If none of them is there, **the sample downloads the list itself** — the same source, the same place, the same
   number of words (685,829, checked after the download). The download happens in the background of the fill, and the panel says
   what it is doing while it runs.

   The sample does *not* fall back to the curated subset silently: a sample that offers 7,071 words where 685,829 are available
   is a lie by omission, and the number of entries the user asked for would not be the number the index holds. The curated subset
   is what the *tests* use, and the tests point the sample at it (the tests never download 5.5 MB).

   The panel names the source and the size of the list, before it is there: "685,829 words (downloaded when the index is
   filled)".

6. **Bogus is a dependency of the sample alone.** The sample is not shipped (`IsPackable=false`); the two packages keep their
   dependency set, which is the contract of ADR-0004 and ADR-0018. The version lives in `Directory.Packages.props` like every
   other version of the repository — the first dependency the sample has, and the reason this record exists.

7. **The typo tolerance is on for `people` only.** A name is what a user misspells; a dictionary of 50,000 names costs a few
   megabytes. A dictionary of 685,829 words costs about 140 MB (ADR-0021) — too much for a demo that may be filled to the end of
   the list, so `words` and `hashtags` correct nothing.

8. **The number of entries is the user's, where it makes sense.** The panel offers a number field per bulk index: for `words`
   the ceiling is the size of the list (685,829 words), for the two Bogus indexes there is none — 3,000,000 is the largest fill
   the sample offers, the scale of the measurements of the specification (ADR-0020). The default is 10,000 entries — or the whole
   list when it is shorter — about a second, enough to see the difference against 25 entries. The user who wants to see the why
   of LUI asks for a million: the fill takes a minute, the query stays in the milliseconds.

9. **The page is two columns, and the settings have the right one.** The autocomplete and everything that belongs to it stand
   on the left; the settings of the query stand on the right, in their own column, and stay there while the page scrolls — a
   sticky panel that lies over what is below it is the one thing the panel must not do. The panel of the settings is 690 px
   tall (the explanation of a setting is behind its "?" and is its tooltip as well), so it fits a screen without a scroll of
   its own, and every field is wide enough for what it shows (`WordStartThenSimilarity` fits; a select that cannot show its
   value says it in its tooltip).

10. **Every index has a tab, and the button that fills it stands in that tab.** The panel of the indexes is a tab bar with one
    tab per index and the count it holds — six cards in a column are six things to read, one card is one thing. The tab of the
    autocomplete of an index holds what fills it: the number of entries, the button, and the progress — where the index is
    used, not somewhere below it. The panel of the indexes keeps the maintenance of every index (`Sync`, `Check`, `Rebuild`,
    `Optimize`, the typo dictionary) — and the entries of a bulk index are the ones of its source: there is nothing to write
    into it by hand.

11. **One thing that breaks does not take the rest of the page down.** A panel that cannot read its data says so and keeps
    what it had; the handler of an event that throws does not stop the other handlers (the hits of a query do not vanish
    because the panel of the indexes could not be read); an input that cannot be built says so instead of standing there
    silent; and the route of the indexes describes every index separately, so one broken index does not empty the panel. An
    empty panel looks like data that was lost — the failure must be visible, and it must be local.

12. **The catalog of the sample is configured the way ADR-0020 measures it**, and the fill uses the batch of ADR-0016: a page cache of
    512 MB, a memory-mapped file, and 100,000 entries per batch — a first load into a file that no one else writes is about 30 %
    faster with that batch than with the default of 10,000. The sample is not a benchmark: it is the demonstration of a big load,
    and it is configured like one.

## Consequences

- **The sample shows the why of LUI now**: open the tab of an index, fill it with 685,829 words — or a million names — and
  type into it: the query stays in the milliseconds while the index holds a million rows. That is the motivation of the
  library, in the page.
- **Nothing is paid for by default.** A fresh clone starts with the three curated indexes; the database file stays small; the
  memory of the process stays small. The bulk indexes do not even exist until the user asked for them — the fill creates them.
- **The fill is a background task of the sample** (`Task.Run` on a thread of the pool), not a hosted service of the library:
  the library offers `Sync` with a progress, and the sample decides how the work is scheduled. A fill that is cancelled keeps the
  batches it committed (ADR-0016) — the next fill continues.
- **The sample depends on Bogus.** The `Repository_Layout` guards keep the versions in one place and every pin referenced; the
  dependency set of the two packages is unchanged, and `PackageIdentityTests` still holds the shipped assemblies to their
  documented set.
- **The word list is not in the repository** — the sample finds it where the repository's own tools put it, and downloads it when
  it is not there. The panel names the source, so a reader knows which list he is playing with.
- **The sample needs the internet once** — for the list of 5.5 MB, and only when the `words` index is filled. Everything else
  works offline, and the two Bogus indexes do as well.
- **The sample's API grows**: three routes for the fill, and three fields in the description of an index (the source of its
  entries, their ceiling and what is wrong with it). Nothing of the library's public API changes (ADR-0026): this is the
  sample's own API.
- **Reversible:** the three indexes, the three routes, the download and the dependency of the sample can go without a trace; the
  library does not know them.
