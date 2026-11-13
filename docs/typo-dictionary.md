# The typo dictionary

> How the memory of the correction is built, and what every distance costs. The
> distances are an enumeration (`LuiTypoDistance`) and not a free number, because
> the dictionary grows with every additional error — so the numbers below are
> the reason the enumeration has three values and not an `int`.

---

## 1. What the dictionary holds

The correction answers a question that FTS5 cannot: the input has no word
start, but it is one or two letters away from one. The answer comes from a
dictionary of the **word beginnings** of the index.

| Step | What happens |
|---|---|
| The text of every entry | is normalised into its search form — the same form FTS5 indexes ([the normalisation](./normalization.md)) |
| Its words | are cut: every word of three letters or more contributes its **beginnings of 3 to 10 letters** (`apf`, `apfe`, `apfel`, …, `apfelkuche`) |
| Every beginning | goes into a [SymSpell](https://github.com/wolfgarbe/SymSpell) dictionary, with the weight of its text |

The dictionary of SymSpell is a **symmetric delete** dictionary: for every word
it keeps the word itself and its *delete variants* — the words that come from it
by deleting one, two, … letters. A lookup deletes the letters of the input in
the same way and looks the deletes up. The number of the delete variants is
what makes the **second** error expensive:

| Distance | Delete variants per beginning | What is forgiven |
|---|---|---|
| `None` | none — **LUI builds no dictionary at all** | nothing |
| `OneError` | the word + its deletions of one letter (7 with a prefix length of 7) | a letter left out, one too many, one wrong, two swapped |
| `TwoErrors` | the word + its deletions of one letter + its deletions of two (21 more) | two of the above |

**The distance `None` is free because LUI builds nothing — not because the
dictionary of SymSpell would be small.** SymSpell keeps the *same* delete
variants at the distance 0 as at the distance 1: the distance says how many
deletions a **lookup** accepts, and the delete variants of the dictionary are
the ones of one deletion either way. Measured over the beginnings of the corpus
of the measurement (647,948 of them): **880,384 entries at the distance 0 and
at the distance 1**, 1,367,891 at the distance 2. A dictionary of the distance 0
would cost the memory of the one of the distance 1 — and it could never correct
anything.

The prefix length of the dictionary is **7**: SymSpell keys a word by its first
seven letters, and the correction looks a word up by its first ten letters
(`LongestBeginning`), so a word longer than ten letters is corrected by its
beginning and keeps the rest.

## 2. What every distance costs

The numbers were **measured**, not estimated — the word list of the repository
(685,829 words, the list that [`tools/fetch-wordlist.sh`](../tools/fetch-wordlist.sh)
fetches; 685,739 of them are used: the words of three letters or more, without
a digit), one index and one database per distance.

| Distance | The dictionary | The build | The memory |
|---|---|---|---|
| `None` | **not built** — the state stays `NotBuilt` | **0.0 s** | **0 MB** |
| `OneError` | built | **4.3 s** | **138 MB** |
| `TwoErrors` | built | **8.5 s** | **240 MB** |

**The time belongs to the machine and to the run below** (ADR-0041): it is a
measurement, not a promise, and it is not reproducible — a parallel job on the
same machine makes it twice as slow (a second run of the same day, with a
parallel build in the same sandbox, took 4.4 s and 8.7 s). **The memory is the
claim that does not depend on the machine**: 138 MB and 240 MB are the sizes of
the dictionaries, and they come out the same on every machine.

Two errors cost **1.7 times** the memory of one and **twice** the time — and
**40 MB more than the budget of ADR-0020** allows (ADR-0020: *typo dictionary, 700,000
different words: ≤ 200 MB, build ≤ 10 s*). The build time is still inside the
budget; the memory is not. The budget was written for one error, and it holds
for one error. The two-error distance has **no budget of its own yet**: the
budgets move into `budgets.json` with pull request 3 of the series, and the
distance gets its budget there. The miss stands written down until then.

The row of one error is the number of ADR-0021 — the same word list, the same
138 MB. A measurement that reproduces the old
number is a measurement that is comparable with the old one.

### The machine, the corpus, the method

| What | Value |
|---|---|
| The date | 2026-11-12 |
| The corpus | `artifacts/wordlist/german-words.txt` — 685,829 words, the German word list of [enz/german-wordlist](https://github.com/enz/german-wordlist) (CC0) |
| The machine | Intel Xeon Platinum, 2 cores, 3.0 GB RAM, Debian 12 (bookworm), .NET 10.0.403 — the load average of the machine was 1.1 during the run |
| The method | the growth of the heap of the process while the dictionary is built (`GC.GetTotalMemory(forceFullCollection: true)` before and after `WarmUpTypoDictionary()`), one index and one database per distance in **one run of the process** — the heap is read with a forced collection, so the dictionary of the previous distance is reclaimed before the next one is measured |
| The command | `dotnet test tests/Nordmann.LUI.Performance.Tests --configuration Release --filter "FullyQualifiedName~TypoPerformance.What_every_distance_costs" --logger "console;verbosity=detailed"` |

The memory is the size of the dictionary, not the size of the index: the index
of 685,739 words is in the file, and the file is not in the heap.

## 3. What the application pays for

The dictionary is **not** built by a query — the application asks for it
(ADR-0042),
with `WarmUpTypoDictionary()` or through the warm-up of the host (ADR-0043).

- **A distance of `None` pays nothing.** There is no dictionary, and the state
  of the dictionary stays `NotBuilt` — the proof is that the source of the
  dictionary is never read, not a measurement of a heap. The guard sits where
  the dictionary is **built**, so every path that reaches a build pays nothing:
  the warm-up, the rebuild (`RebuildTypoDictionary()`) and the sync of a linked
  index.
- **The dictionary only grows** (ADR-0025):
  a word that is written after the build is learned at once, a word that is
  removed stays until the dictionary is built anew.
- **The dictionary belongs to the index of the catalog**, not to an instance of
  it: two instances of an index share it, two indexes never correct towards
  each other's words.

## 4. The decisions this document carries

- **The distance is an enumeration** (`None`, `OneError`, `TwoErrors`) and not a
  free number: the possible distances are the ones that were measured, and the
  numbers above are what a caller decides with. A third error would need a
  third measurement.
- **The distance belongs to the index**, not to the query: the dictionary is
  built once, with the distance of the index, and a query can only switch the
  correction on or off (`Query(text).TypoTolerance(…)`). The distance is part of
  the stored definition, so an index that is opened again forgives what it
  forgave before.
- **`None` builds no dictionary.** 138 MB for a correction that is never made
  is not a setting.

The decision itself is ADR-0054.
