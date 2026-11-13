# Word corpus for the normalisation parity test

LUI's normalisation rule exists **twice** — once as SQL expressions in the
index columns, once as a C# `Normalizer` (ADR-0005). The two must produce the
same search form, or the library silently fails to find things.

That is why this corpus exists.

| File | What it is | Size |
|---|---|---|
| `german-words.sample.txt` | every 97th word of the German word list — 7,071 words, deterministic | ~88 KB |
| `edge-cases.txt` | the characters a stride cannot reach for: umlauts, ß, accents, ligatures, decomposed forms | ~1 KB |

## The source

The word list is the **"Deutsche Wörterliste" with 685,829 entries** — the
number the specification names (ADR-0019, docs/testing.md).

| | |
|---|---|
| Source | <https://github.com/enz/german-wordlist> (`words`, UTF-8, one word per line) |
| Licence | **CC0 1.0 Universal** — public domain dedication, no conditions |
| Entries | 685,829 — matches ADR-0019, docs/testing.md exactly |
| Origin | a mirror of the University of Leipzig's list; the Leipzig download itself (`https://www.wortschatz.uni-leipzig.de/download/Deminck`) sits behind an anti-bot wall and is **not** reachable from a script |

The full list is **not** checked in: 5.5 MB of data that nobody reviews. The
sample is checked in, and it is generated, not curated — a hand-picked subset
drifts towards the letters the author already thought of, and the parity test
is exactly where the *unthought-of* letters fail.

## The full run

```bash
tools/fetch-wordlist.sh        # → artifacts/wordlist/german-words.txt (gitignored)
```

The parity test uses the full list when it is present (via `LUI_WORDLIST` or
the default path) and always runs the checked-in sample. The full run is the
release gate; the sample is the pull-request gate.

## Regenerating the sample

```bash
tools/fetch-wordlist.sh
tools/make-curated-corpus.sh
```

The stride (97) is part of the contract: changing it changes the corpus, and
the change is visible in the diff.
