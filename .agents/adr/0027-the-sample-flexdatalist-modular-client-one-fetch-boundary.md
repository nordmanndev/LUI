# 0027 — The sample: flexdatalist against the whole API, a modular client, one fetch boundary

> **Contract note (ADR-0056, 2026-11-12):** this record was written against a contract outside the repository — a
> private one, that no reader of LUI can know and does not have to. The repository states its own rules since: the
> rules that contract carried for LUI stand in [`AGENTS.md`](../../AGENTS.md) and in the documents of
> [`docs/`](../../docs/README.md). The text below is history and stays as it was written.

- **Status:** Accepted (owner's requirements of 2026-11-04: built with flexdatalist, a clean modular client, the whole of LUI, a payload, highlighting and the abort of a fast typeahead "professionally"; highlighting was then dropped on the owner's decision, decision 4 and ADR-0028)
- **Date:** 2026-11-04
- **Issue:** #51 (PR-011 of the plan)
- **Builds on:** [0002](./0002-library-not-an-application.md) (the sample is a library demo), [0017](./0017-dependency-injection-and-hosted-services.md)
  and [0018](./0018-two-packages-core-and-hosting.md) (the sample uses the hosting package), [0025](./0025-the-typo-dictionary-only-grows.md)
  (a dictionary per index), [0026](./0026-the-public-api-from-0-1-0-on-grows-only-additively.md) (written against the frozen API), [0028](./0028-lui-has-no-highlight-a-correction-is-shown-as-a-question.md) (no highlighting)
- **Amends:** plan §3 D7 ("one `index.html` with inline `<style>` and an inline module") and the corresponding row of ADR-0002
- **Affects:**
  - `samples/Lui.Sample/` (the server, the client, `client-tests/`, `package.json`)
  - `tests/Nordmann.LUI.Integration.Tests/` (`SampleApiTests`, `SampleHost`; a framework reference, a project reference),
    `tests/Nordmann.LUI.Architecture.Tests/Sample_Client.cs`, `tests/Nordmann.LUI.Testing/Repository.cs`
  - `.github/workflows/ci.yml` (a lane for the client's tests), `tools/mutation-check.sh`, `docs/testing.md`, `README.md`

## Context

The sample is what people see first. The plan (D7) wanted one inline page that shows the whole API, built with flexdatalist; the
owner now asks for more than the plan: **several autocompletes** that cover every scenario of LUI, the **query settings** passed
along, a **payload** (a category) that flexdatalist makes visible, a **clean client in modules**, and flexdatalist used
**professionally** — highlighting (dropped, decision 4), and the abort of a typeahead that types fast. A sample that is a hundred lines of inline
script teaches how to write a demo; a sample in modules teaches how to use LUI in an application.

### What flexdatalist 3.1.1 does and does not do — read in its source, not in its README

| Topic | The library | Consequence here |
|---|---|---|
| Highlighting | Marks only in its own client-side filter: `<property>_highlight` with `<span class="highlight">`. With `searchDisabled: true` — which a server-side ranker needs: its filter would drop `gedekter` → `Gedeckter Apfelkuchen`, and `mueller` → `Müller`, because it cannot match them literally — **nothing is marked**; and filter and marker are one function, so one cannot be had without the other. | The page does not mark: a correction is shown as "Did you mean …?" (decision 4). LUI has no `Highlight` ([0028](./0028-lui-has-no-highlight-a-correction-is-shown-as-a-question.md)). |
| Writing the list | The values of `visibleProperties` **and the group header** are written with `innerHTML`; without a `_highlight` twin the **raw value**. | Every displayed property has an escaped `_highlight` twin (`suggest/item.js`; the name is the library's, the twin carries no markup). A hostile entry (`<img onerror>`) is text. |
| Aborting | None. It debounces (`searchDelay`) and keeps **one** request pending while one is in flight; the answer of the one in flight is still rendered for an input the user has left. | The fetch boundary aborts it (decision 3). |
| Cache | `cache: true`; the key holds the keyword and the relatives, **not** `params`. A changed query setting would show the hits of the old one. | `cache: false`. |
| The response | Only `resultsProperty` is read; the rest of the response is dropped. | `CorrectedQuery`, `Truncated`, `Timings` would be lost: the boundary puts the whole `LuiResult` on a bus. |
| A refused request | The body is taken as the results; a problem document would be iterated as data. | An empty list for the library, the reason for the panels. |
| Styles | It positions the list by script (`top`, `left`, `width`) and expects `position: absolute` from its stylesheet; `cssText` and `style.*` are CSSOM calls. | The sample ships its own stylesheet for the class names the library uses (plan D7), and a strict policy works with it (decision 5). |

## Decision

### 1. flexdatalist 3.1.1, vendored, unmodified, JS only

`wwwroot/js/vendor/flexdatalist.es.js` is the ES build of the npm package, byte for byte; `vendor/README.md` names the source, the
version, the licence (MIT) and the SHA-256, and `Sample_Client` fails when the file and the hash disagree. No CSS of the library
is used. No CDN, no bundler, no `npm install`: the client has no dependencies and no build.

### 2. A client in modules, separated by concern, with the direction of the imports as a table

```text
wwwroot/js/
  main.js            the composition root: the only module that knows all the others
  core/              bus (events), dom (build DOM from text, never from markup), format
  net/               http (the fetch boundary, part one), api (the routes), suggest-transport (part two)
  suggest/           settings (the knobs, the defaults from the server), flex-options, item (escaped twins), did-you-mean, swatches
  scenarios/         one module per autocomplete: data, a presenter, the options flexdatalist gets
  panels/            settings, scenario-view (the one module that uses flexdatalist), envelope, hits, activity, indexes
  vendor/            flexdatalist
```

`core` imports `core`; `net` imports `core`; `suggest` imports `core`; `scenarios` imports `core` and `suggest`; `panels` import
all of them; `main.js` imports all but the vendor. The panels know each other not at all: they listen to the bus. `Sample_Client`
checks the table, that every import resolves, that no module is dead, and that flexdatalist is imported in one place.

### 3. One fetch boundary: `net/`

`fetch` appears in `net/http.js` (every request that is not a suggestion) and `net/suggest-transport.js` (every suggestion) and
nowhere else — a guard. flexdatalist calls the global `fetch`; the transport stands in front of it **for `/api/suggest/` only**
and passes everything else on. It does what the library does not:

- **Aborts.** A new request for an index aborts the one in flight; a keystroke (`input` on the alias) calls `cancel(index)` before
  the debounce has even fired. The request's token reaches `LuiIndex.Search`, so the server stops its work too — the page shows
  its own count of aborts beside the server's (`/api/health`), and `SampleApiTests` proves a closed connection ends the request.
- **Publishes the envelope** (`suggest:response`) for the panels, and **makes the items** with the presenter of the index.
- **Turns a refusal into an empty list** (and an event with the reason).

Why a transport and not a patch of the vendored file: the file stays byte-identical to the package, and the page owns the
behaviour that is the page's. Why the global `fetch`: it is what the library calls; there is no hook.

### 4. No highlighting; a correction is shown as a question

The sample marks nothing. A first version did — a fold table measured from the tokenizer, a span map from the search form back to
the original, 600 lines with tests, exact for 334 letters of 16 Latin alphabets — and still could not mark what LUI matched through
the compact form or the second correction variant. The owner asked what a user needs from a correction, and the answer is not a
mark: it is to know that a correction happened. Hence `suggest/did-you-mean.js`: when `LuiResult.CorrectedQuery` is there and
differs from what was typed, **one line above the list** says "Did you mean “apfel”?", and choosing it searches the corrected word as
it is. The list is what LUI ranked; flexdatalist's own filter and marker stay off. LUI delivers no found positions
([0028](./0028-lui-has-no-highlight-a-correction-is-shown-as-a-question.md)).

What stays is the **safety** the missing marker would have hidden: flexdatalist writes the displayed values with `innerHTML` and,
without a `<property>_highlight`, the raw value — so every displayed property keeps its escaped twin (decision 5).

### 5. Safe by construction, and by policy

The server sends `Content-Security-Policy: default-src 'self'; script-src 'self'; style-src 'self'; …` — no inline script, no
inline style, no `unsafe-*`. So: the page has no inline script, style or handler (a guard); the panels build DOM from text nodes
(`core/dom.js`; a guard forbids `innerHTML` and friends in the page's own modules); the colour of a tag (a column of the
application's table) is validated as `#rrggbb` and set through the CSSOM after the list is drawn; and every displayed property
has an escaped twin. The three layers are independent: the check in the browser made a hostile recipe, category, tag and kind
literal text, and no element is created from the data even where the policy is not looked at.

### 6. Three scenarios, three ways an application uses LUI

| Index | Storage · kind | Payload | What it shows in flexdatalist |
|---|---|---|---|
| `recipes` | Embedded · MultiWord | JSON `{category, minutes, emoji}`, stored by LUI | `groupBy: category` with counts, the emoji and the time as properties, a badge for a priority above normal, typo correction as "Did you mean", transliteration |
| `tags` | **Linked** to the application's `tag` table · SingleWord | columns `kind`, `color` (and `usage_count` as the weight, `rank` as the priority) | a **tag picker**: `multiple`, `selectionRequired`, grouped by kind, a colour dot from the payload; `vegan` (System) leads; `showAddNewItem` writes a row to the table and tells the index (triggers → outbox → `ApplyPending`) |
| `names` | Embedded · MultiWord | none | the typo tolerance; a dictionary of its own — `megn` is `mega…` here and nothing among the tags (the owner's requirement of #48, seen) |

### 7. Every query setting is a parameter; every default is the library's

`GET /api/suggest/{index}?q=…&take&candidates&rank&priorityMode&distinct&similarity&typo&timings` maps one to one onto
`LuiQueryOptions`; a missing parameter is the library's default, which the page reads from `/api/meta` (`new LuiQueryOptions()`),
not from a copy of its own. flexdatalist's own settings (`minLength`, `searchDelay`, `maxShownResults`, `groupBy`) are a second
group in the panel, kept apart because they belong to another library. The control panel stops `Candidates` at 500, the
budget of AGENTS.md. `delayMs` (0 … 2000) is a demo parameter: a slow server, to see an abort.

**Not offered, because it does not exist:** a `Highlight` setting and a highlight span in the hit table (the plan's D7 table had
them; struck, [0028](./0028-lui-has-no-highlight-a-correction-is-shown-as-a-question.md)). **Not built:** a list of the entries of an index (the library has no such
call), and a `POST` form of the suggest route (every setting is a scalar).

### 8. The server

The sample uses `Nordmann.LUI.Hosting`: `AddLui`, `AddIndex`, `AddLinkedIndex(…, applyPendingEvery)`,
`WarmUpTypoDictionariesOnStart`. The application's `tag` table is created before the host starts (ADR-0015, ADR-0017); the
application creates the triggers on purpose. A facade per storage — `EmbeddedSampleIndex<TPayload>`, `LinkedSampleIndex<TPayload>`
— keeps the generic payload out of the routes and the two ways of keeping an index current visibly different. Routes:
`/api/meta`, `/api/indexes`, `/api/indexes/{name}`, the write side (`/entries`, `/entries/{key}`, `/rows`, `/rows/{id}`), the
maintenance (`/sync`, `/check[?source=seed]`, `/rebuild`, `/optimize`, `/typos/warm-up`, `/typos/rebuild`, `/refresh`,
`/apply-pending`, `/triggers`), `/api/suggest/{index}`, `/api/health`. The routes are plural (`/api/indexes`), unlike the plan's
sketch. A wrong argument is a 400, a condition LUI reports (`LuiException`) a 409, both as problem details.

### 9. Tests

| Layer | What |
|---|---|
| .NET integration | `SampleApiTests` (35): a real Kestrel on the loopback over a database of its own — every setting reaches the library, the three indexes, the dictionary per index, the write side, the linked flow with and without triggers, an abandoned request, the policy |
| .NET architecture | `Sample_Client` (16): the rules of decision 2, 3 and 5 and the hash of decision 1; five mutations in `tools/mutation-check.sh` |
| Node (`npm test`) | 32 tests of the modules without a browser: every presenter against hostile data, the settings, the transport (abort, cancel, envelope, refusal), "Did you mean", the bus; a CI lane |
| Browser | Chromium, by hand for this pull request (Playwright is not a dependency of the repository): typo correction and "Did you mean" (and choosing it), transliteration, groups, the picker and add-new, abort while typing fast, a hostile entry, the settings, the indexes panel — described in the pull request |

The integration project hosts the sample: a project reference and the ASP.NET Core **framework** reference, no package;
`Directory.Packages.props` is unchanged. The framework brings `Microsoft.Extensions.Hosting` along, so NuGet calls that test
package redundant (NU1510); it stays, with the warning suppressed on that reference, because the smoke test reads its version
from the props file and a guard wants every central version referenced.

## Consequences

- **The sample is a reference for an application**, not only a demo: the composition, the payload, the two kinds of index, the
  outbox, a client that treats a fast typeahead and hostile data correctly. Its client is not a framework: it is the shape.
- **The sample trusts nobody and protects nothing.** It has no authentication, anybody may write to its indexes, and a slow
  server is a parameter. It is for `localhost`; it is not a template for an internet-facing page.
- **flexdatalist is pinned by hash.** A new version is a deliberate change: the provenance, the hash, a read of what changed in
  highlighting, caching and requests, and the browser check. The sample depends on three behaviours of it, and each is in the table above.
- **A page that wants marks** writes them itself against `CorrectedQuery` and the hit text; the sample no longer shows how (0028).
- **The browser check is not in CI.** The node tests, the .NET tests and the guards cover what can be covered without a browser;
  what only a browser shows (the CSS the library expects, the policy, the list under the input) was seen once, and a regression there
  would be seen by the next person who runs the sample.
- **Rejected:** a framework (Alpine, as the standards' baseline, ADR-0002: it would teach nothing about LUI); a bundler; the
  library's own CSS (plan D7); letting flexdatalist filter (it would drop what the server corrected); a patch of the vendored
  file for aborting (the page owns that behaviour; the file stays verifiable); `unsafe-inline` for the colour dots (the CSSOM does it); a
  page-made highlight (built, measured, and dropped: 0028).
