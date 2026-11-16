# Plan — The sample is a page per dataset

- **Date:** 2026-11-16
- **Base branch:** `dev` (the sample does not depend on the round of the measured work; the plan sits beside it)
- **Decided by:** the owner, on 2026-11-16, in the review of MVP.css: *use mvp, rewrite all with the same functionality of
  searching, make an index file, and give each sample dataset its own html file* — and then: ***no custom css, just mvp, only
  the layout is new; flexdatalist stays, that is all*** — and then: ***the styling, where possible, follows the attached brand
  guide*** (the Timetables brand book v1.0.0 and the two logo files).
- **Issues:** to be created — one epic and the issues of the six pull requests below
- **Status:** planned — nothing of it is written yet
- **Why now:** the sample is the first thing a reader of LUI sees (ADR-0027), and it is the one part of the repository that
  shows the whole API surface at once.

## 1. The goal

**The sample becomes seven pages: one per dataset, plus an index.** The client is rewritten in MVP.css's own vocabulary, the
**client writes no style of its own** — the layout comes from MVP, the dropdown from the stylesheet flexdatalist itself ships,
and the **brand of the guide** comes in through the variables of those two files. The search keeps the whole of its behaviour:
the typo correction, the "Did you mean", the payload, the groups, the picker, the abort of a fast typeahead, the hostile entry,
the timings, the priority, the fill of a bulk index.

| The page | The dataset | What it shows |
|---|---|---|
| `index.html` | — | the six datasets as links, and the **indexes panel**: the six indexes with their state, their maintenance, and the fill of the three bulk ones |
| `recipes.html` | `recipes` | embedded · multi-word · JSON payload |
| `tags.html` | `tags` | linked to the application's own table · column payload · priority |
| `names.html` | `names` | embedded · multi-word · no payload · typo tolerance |
| `words.html` | `words` | embedded · single word · the German word list (bulk) |
| `people.html` | `people` | embedded · multi-word · Bogus's German names (bulk) |
| `hashtags.html` | `hashtags` | embedded · single word · Bogus's German tags (bulk) |

## 2. What the sample is today

The facts the plan is built on. Each of them is measured in the repository, in the package or in the brand guide — not
remembered.

- **The client is static files** — `samples/Lui.Sample/wwwroot/`: one `index.html` (44 lines), five CSS files (313 lines,
  **15,001 bytes**), 26 own ES modules (1,693 lines) and the vendored flexdatalist (1,807 lines, 3.1.1, MIT, hash-pinned).
  No build, no bundler, no dependency (`samples/Lui.Sample/package.json` exists only so Node can run the client tests).
- **The styling is class-based**: of the 148 rules in the five CSS files, **128 are class selectors (86 %)** and 20 are element
  selectors. `css/base.css` 1,132 B · `css/components.css` 7,403 B · `css/flexdatalist.css` 4,243 B · `css/layout.css` 1,060 B
  · `css/tokens.css` 1,163 B (24 tokens, plus a dark mode of its own).
- **MVP.css v1.18** is 10,264 bytes, 609 lines, **135 selectors and no class selector at all** — it styles root elements only.
  Its own template (`mvp.html`) is `header > nav > ul`, `main > section > aside` (the card), `section > table`,
  `section > form`, `article`, `details > summary`, `mark`, `code`, `pre`.
- **flexdatalist ships its own stylesheet** — `flexdatalist@3.1.1/dist/flexdatalist.css`, 6,211 bytes, MIT, SHA-256
  `449b37f0e0a83b15aa52939593534d8f60bd1227abf9b17ade1db0ef28527e70`. It is the file the sample does **not** use today
  (`wwwroot/js/vendor/README.md`: *"CSS not vendored. The sample writes its own"*), and it is what makes the dropdown work
  without a line of CSS from us: it sets `.flexdatalist-results { position:absolute; top:0; left:0; z-index:10000; … }` — the
  library places the list by script (`flexdatalist.es.js:1415` sets `width`, `top`, `left`) and **never sets `position`
  itself**, so the stylesheet has to. It also styles the chips, the group header, the active row and the loading line, and it
  has its own dark mode (`@media(prefers-color-scheme: dark)`) and its own palette (`--fdl-accent`, `--fdl-bg`, `--fdl-border`,
  `--fdl-text`, `--fdl-tag-bg`, `--fdl-tag-border`, `--fdl-tag-fg`, `--fdl-group-bg`, `--fdl-group-border`, `--fdl-shadow`,
  `--fdl-radius`, `--fdl-accent-light`, `--fdl-accent-fg`, `--fdl-text-muted`).
- **The markup is built in JavaScript**: **165 `el()` calls, 80 `class:` properties, 73 distinct (tag, class) pairs** —
  `indexes-panel.js` 44 el / 26 class, `scenario-view.js` 18/13, `hits-panel.js` 16/7, `settings-panel.js` 14/5,
  `envelope-panel.js` 14/11, `fill-form.js` 11/9.
- **The rules the repository holds the client to**: the CSP `default-src 'self'; script-src 'self'; style-src 'self';
  img-src 'self' data:` (`samples/Lui.Sample/SampleApp.cs:135`), the guard
  `The_page_has_no_inline_script_style_or_event_handler_and_loads_nothing_from_elsewhere`
  (`tests/Nordmann.LUI.Architecture.Tests/SampleClientTests.cs:143`), the vendored-file guard (`:177`), the guard that the
  client has no dependencies and no build (`:197`), and the node tests of the client (43, all green on 2026-11-16 — ADR-0027
  still names 32, from the day the client was written).
- **ADR-0027** decides the client (one fetch boundary, the module tree, the vendored library, no CDN, no bundler, no
  framework) and **ADR-0029** decides the page (the two columns with the sticky settings, the fill in the tab of the index
  it fills).

### What the brand guide says (read, not assumed)

- **The palette** (chapter 4, PDF pages 34–36): the primary colours are **Cobalt Blue `#1a67d8`**, Cetacean Blue `#1d90d6`,
  Cerulean Frost `#1dafd6`, **Midnight Navy `#00002d`**, Sunshine Yellow `#ffc700`, Soft Amber `#ffdf7b`, Pale Cream `#fff1ca`,
  Deep Slate Blue `#333357`; the secondary palette is Ghost White `#f1f1f1`, Silver Sand `#d0d0d3`, Cool Slate `#bcbcc9`,
  Pure Black `#000000`, Pure White `#ffffff`, Black Coffee `#1a1a42`, Dark Bistre `#443b2c`. The documented tints of the cobalt
  blue are `#3176dc #4885e0 #5f95e4 #76a4e8 #8db3ec #a3c2ef #bad1f3 #d1e1f7 #e8f0fb`, the tints of the midnight navy
  `#333357 #666681 #9999ab #ccccd5`. Two rules of the guide bind the page: **the colours are only ever used at 100 % opacity**
  and **on screen the RGB values are used, never Pantone or CMYK**.
- **The typefaces** (chapter 3, PDF pages 24–31): **Montserrat** is the primary (body copy in Regular and Light), Gotham is the
  secondary (headings and the logo wordmark) — Gotham is a commercial licence (Hoefler & Frere-Jones) and **cannot be put on
  a web page**, so the headings take Montserrat, which is what the guide's own logo wordmark is set in (Montserrat ExtraBold,
  PDF page 14). **Roboto Slab** is the tertiary, *"used specifically for captions, call-outs, sidebars, and data visualizations"*
  — the sample's hits table and timings table are data visualizations. Noto Naskh Arabic and Noto Sans Traditional Chinese are
  support faces for those scripts.
- **The fonts measured** (Google Fonts, Latin subset, variable): **Montserrat 37,956 B**, SHA-256
  `06b16db7a969135d48d38c49183be7fb88d4452e2a3011957c7851941f4e4879`, **SIL Open Font License 1.1** (4,400 B) — **the
  one that is vendored**. **Roboto Slab 34,236 B**, SHA-256 `50cf7d32c18e1001749b51f1565ef745bc0687b0297b2b13465ed577cc20a8ff`,
  **Apache License 2.0** (11,358 B) — **not vendored**: it is the tertiary typeface of the guide, and it would have to reach
  the tables through a `table { font-family: … }` rule, which is a style of our own (D3). The deviation is in §8.
- **The logo** (chapter 2, PDF pages 8–21): the mark is a *T* built from 12 equal squares, the wordmark is Montserrat ExtraBold,
  the guide names the colour of the mark `#017efa` and of the wordmark `#00002d`, and the rule is *"the White version should be
  used for dark backgrounds and the Black version should be used on light backgrounds"*; the exclusion zone is 50 % of the
  logo height (stacked) or 100 % (horizontal).
- **The two attached logo files are the Nordmann Development logo, not the Timetables logo**: `1.svg` (5,136 B, 20 paths) is the
  full lockup (the blue mark + "Nordmann" + "DEVELOPMENT"), `2.svg` (3,207 B, 9 paths) is the mark + "Nordmann". Both are
  Illustrator exports in **`#267EC7` (the mark) and `#333333` (the wordmark)** — the colourway of the company, not of the
  guide. **Both carry a large empty canvas**: the artwork is **2,196 × 396** inside a canvas of 3,147 × 2,000 (1.svg) and
  3,220 × 2,000 (2.svg) — **86 % of the canvas is empty** — and the lockup is **5.55 : 1** (wide). The viewBox therefore has
  to be cropped to the artwork (plus the clear space) before the logo can stand in a header. The guide's own Timetables logo
  is **vector inside the PDF** (the logo pages carry no raster image — the whole guide has 48 of them and 39 of those sit in
  the mockups chapter, page 43 — and `pdftocairo -svg -f 21 -l 21` lifts the page out as 114 KB of SVG with 140 paths and 250
  glyph uses), and could be extracted; **it is not used** (the owner's decision: the company's logo stands in the header).

## 3. The decisions

| # | The decision | Why |
|---|---|---|
| D1 | **The sample is seven pages**: an index and one per dataset. The tab strip of the six datasets is gone; the navigation between the datasets is the `nav` of the pages. | The owner's answer of 2026-11-16. A tab strip is MVP's `nav` in the wrong direction: MVP's navigation is between pages, and the dataset is then the page. |
| D2 | **The layout comes from two vendored stylesheets, and neither is written by us.** `wwwroot/css/mvp.css` (MVP.css v1.18, 10,264 B, MIT, SHA-256 `e0843dbd4883d1d30ef62102ff498a1698deab8d435f889e2b2be0288a45cccd`) and `wwwroot/css/vendor/flexdatalist.css` (the file the library ships, 3.1.1, 6,211 B, MIT, SHA-256 `449b37f0e0a83b15aa52939593534d8f60bd1227abf9b17ade1db0ef28527e70`). Both vendored byte for byte, both named with their provenance and their hash in `wwwroot/css/README.md`, both held to it by a guard. **No CDN.** | The owner's answer: *"i do not wanna use any custom css, just mvp"*. The stylesheet of the library is the library's, and it is what makes the dropdown work: the library places the list by script and never sets `position` — the stylesheet has to, and the library's own file does. The CSP forbids a foreign origin (`style-src 'self'`), and the guard forbids a `<link href="https://…">`. |
| D3 | **The sample writes no style of its own.** `css/base.css`, `css/components.css`, `css/layout.css`, `css/tokens.css` and the hand-written `css/flexdatalist.css` are deleted; the client's modules set **no class of their own** — the classes that exist on the page are the library's. | The owner's answer. A class rule beats an element rule: a stylesheet with classes of its own would win against MVP everywhere, and the two would not add up to one look but to two. With no style of its own there is nothing to win. |
| D4 | **The dark mode is MVP's** (`<html color-mode="user">` on every page); the library's own dark mode follows the system beside it. The sample's own dark mode and its token layer go with the token file. | The owner's answer: *"mvp defaults"*. Both mechanisms follow the operating system, so the dropdown and the page agree. |
| D5 | **The dataset page carries the autocomplete, the query settings, the envelope, the hits and the activity log; the index page carries the links and the indexes panel** (the six indexes, their maintenance, and the fill). | The owner's answer. The settings are about the query, so they belong on the page that queries; the indexes are about what is stored, so they belong on the page that administers it. |
| D6 | **The fill of a bulk index moves to the index page** — it leaves the tab of the index it fills. | A consequence of D1 and D5, and it **amends ADR-0029**: its decision 10 ("the button that fills it stands in that tab") and its decision 9 (the two columns with the sticky settings panel) no longer hold. The reason of decision 9 — *a sticky panel that lies over what is below it is the one thing the panel must not do* — is kept by a different means: the settings sit in the flow of the page, and nothing lies over anything. |
| D7 | **The page names its dataset with `data-scenario` on the body** (`<body data-view="dataset" data-scenario="recipes">`, the index page: `<body data-view="hub">`); one root module reads it. | The CSP forbids an inline script, and the guard forbids an inline `<script>` without `src` — so the identity of the page cannot be a script in the page. One root module keeps the module tree of ADR-0027 valid. |
| D8 | **The information stays, the way it is shown changes**: the segmented timing bar becomes a table of the four phases, the priority pills become `strong` text (`mark` for the System priority), the badges become `small`, the facts and the request log become tables, the "?" of a setting becomes `details`/`summary`, the tab strip becomes the `nav`, the colour dot of a tag becomes a small image with the colour in its data. | D3, taken to its end: with no style of its own, everything that carried information in a colour or a shape has to say it in an element. MVP has an element for a table, a `small`, a `strong`, a `mark` and a `details` — the information does not need a class. |
| D9 | **The work is issue-driven**: one epic and six pull requests, each green, each with its verification, the ADR in the pull request that changes the client. | The rule of the repository (AGENTS.md): work is issue-driven, one pull request per issue, and a decision that changes the dependency set or the client gets an ADR in the same pull request. |
| D10 | **The brand of the guide is applied as far as the two vendored stylesheets allow.** A third stylesheet, `wwwroot/css/brand.css`, is the client's own — and its **only selectors are `:root`** (with or without an attribute), its only at-rules `@font-face` and `@media` around such a selector. It maps the guide's palette onto MVP's `--color-*` (eleven variables) and onto the library's `--fdl-*` (its twenty-one), sets `--font-family` to Montserrat and gives the dark mode the brand's own night values. The two fonts and the two logo files are vendored with their licences and their hashes. The logo in the header is the **company's** lockup (the owner's decision of 2026-11-16): the Nordmann Development mark, not the Timetables logo of the guide. | The owner's answer: *"styling where possible applied to the attached guide"* and *"the company's logo in the header — the guide's palette and type as the styling"*. Both vendored stylesheets are **written to be themed by their variables** — MVP says so in its own words ("Most styles are editable through CSS variables"), and the library exposes its palette the same way. The brand therefore enters **without a single style of its own**: only the values. The guide's rules that the page can keep: the colours at 100 % opacity (every value in the layer is opaque — MVP's `--color-accent` is `#118bee15`, eight digits with an alpha, and the layer replaces it), the RGB values on screen, and the white logo on a dark background. The rules it cannot keep: Gotham (commercial licence), and the exclusion zone around the logo (MVP's spacing is what it is). |

## 4. The vocabulary

The translation of every visual of the sample into MVP's elements — the work that D3, D8 and D10 make. Nothing here is a class:
the sample's own class names are gone.

| The sample today | In MVP's vocabulary | The rule |
|---|---|---|
| `.top` + `nav` (two links) | `header > nav > ul > li > a` | MVP's nav is `display:flex; justify-content:space-between; font-weight:bold; margin-bottom:7rem` (mvp.css:148) |
| `.tabs` / `.tab` (the six datasets) | the `nav` of the pages — one link per dataset | the navigation between pages replaces the switch inside one page |
| `.card` (five panels) | `main > section` (a row) or `article`; the feature cards of MVP are `section > aside` (mvp.css:102) | **`section` is a flex row** (mvp.css:87) — a section that stacks content turns it into a row. The panels that stack are `article`s or `div`s. |
| `.badge` (the kind of the index) | `<small>` — MVP: `small { color: var(--color-text-secondary) }` (mvp.css:285) | small caps |
| `.summary` | `<p>` in the `header` of the page | |
| `.hint` (the buttons to try) | `<button>` with a `<code>` inside | MVP's button is a solid block; `code` is a light chip (mvp.css:298) |
| `.field` (the input) | the flexdatalist input, `input` (mvp.css:341); the list is the library's own | the library's stylesheet does the dropdown |
| `.selection` / `.selected-item` (a `<dl>`) | `<dl>` | MVP has no `dl` rule; the browser's own is enough |
| `.facts` / `.fact` (the facts of the query) | `<section><table>` | MVP styles tables (mvp.css:528) |
| `.bar` / `.seg` (the four-colour timing bar) | a second table: the four phases with their share | **the bar becomes a table** — the same four numbers, readable |
| `.legend` | the `caption` of that table | |
| `.table` (the hits) | `<table>` without a class | MVP's table styling replaces the class |
| `.priority` (the priority column) | `<strong>`, and `<mark>` for the System priority | MVP styles `mark` with a padding (mvp.css:256); the browser gives it the mark colour — *not* the brand's sunshine yellow, which would need a style of its own (see §8) |
| `.request` (the query that was sent) | `<code>` | |
| `.log` / `.entry` (the request log) | `<section><table>` | |
| `.counters` | `<p>` | |
| `.setting` (the grid of the settings) | `<form>` with `<label>` + `<input>`/`<select>` | MVP's own form (mvp.html: `section > form`) |
| `.hint-toggle` (the "?") | `<details><summary>?</summary>` | the explanation is shown when it is asked for — `details` does that natively, and the `title` stays the tooltip |
| `.button` / `.buttons` / `.forms` | `<button>` / `<div>` / `<form>` | |
| `.index-tabs` / `.tab-count` (the six indexes) | a table of the six indexes on the index page | |
| `.index-card` | `<article>` per index | |
| `.fill-form` / `.fill-head` / `.fill-row` | `<form>` + `<progress>` | the fill moves to the index page (D6) |
| `.progress` | `<progress>` | the browser's own |
| `.error` / `.muted` | `<p>` / `<small>` | |
| `.output` | `<pre>` | |
| `.swatch` (the colour dot of a tag) | `<img src="data:image/svg+xml,…" width="12" height="12" alt="">` — the colour is validated as `#rrggbb` and put into the data URI | an empty element without a style has no size; the policy allows `img-src 'self' data:`, so the dot needs no stylesheet |
| the dropdown | **the library's own stylesheet** (`css/vendor/flexdatalist.css`, vendored) | the library's classes are the library's; its file styles them, the sample writes none of them |
| the logo of the header | `<picture>` with two `img`: the company's lockup for the light surface, the same lockup with the white wordmark for the dark one | the rule the guide states for its own logo — *"the White version should be used for dark backgrounds"* — holds for the company's lockup for the same reason: its wordmark is `#333333`, and on the midnight navy that is invisible. The browser chooses by the system, **without a style of our own** |
| the type | Montserrat (the primary) for the text, Roboto Slab (the tertiary) for the two tables | the guide names Gotham for the headings; Gotham is a commercial licence and cannot go on a web page — the guide's own wordmark is set in Montserrat ExtraBold, so Montserrat everywhere is the faithful reading |

**What the stylesheets become:**

| | Today | After |
|---|---|---|
| `css/mvp.css` | — | 10,264 B (vendored) |
| `css/flexdatalist.css` | 4,243 B (written by the sample) | — (deleted in PR 4) |
| `css/vendor/flexdatalist.css` | — | 6,211 B (the library's own file, vendored) |
| `css/brand.css` | — | ~2,000 B (the `:root` layer and the two `@font-face` — the client's own, and its only selector is `:root`) |
| `css/base.css` | 1,132 B | — |
| `css/components.css` | 7,403 B | — |
| `css/layout.css` | 1,060 B | — |
| `css/tokens.css` | 1,163 B | — |
| `fonts/` | — | 37,956 B (Montserrat) + 4,400 B of its licence |
| `img/` | — | the logo and its white wordmark |
| **the total of the stylesheets** | **15,001 B, 313 lines of ours** | **16,475 B, ~60 lines of `:root` only** |

The stylesheets grow by 3,474 bytes and **the sample writes no style of its own** — the two files are vendored, hashed and
named, and the third is the brand's values and nothing else.

## 5. The pages

**A dataset page** — MVP's own shape, with the panels the JavaScript fills:

```html
<!doctype html>
<html lang="en" color-mode="user">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>Recipes — the LUI sample</title>
  <meta name="description" content="Nordmann.LUI: autocomplete on SQLite FTS5, one dataset per page.">
  <link rel="stylesheet" href="/css/mvp.css">
  <link rel="stylesheet" href="/css/vendor/flexdatalist.css">
  <link rel="stylesheet" href="/css/brand.css">
  <script type="module" src="/js/main.js"></script>
</head>
<body data-view="dataset" data-scenario="recipes">
  <header>
    <nav>
      <ul>
        <li>
          <a href="/">
            <picture>
              <source media="(prefers-color-scheme: dark)" srcset="/img/logo-white.svg">
              <img src="/img/logo.svg" alt="Nordmann Development" width="160" height="29">
            </picture>
          </a>
        </li>
        <li><a href="/recipes.html" aria-current="page">Recipes</a></li>
        <li><a href="/tags.html">Tags</a></li>
        <li><a href="/names.html">Names</a></li>
        <li><a href="/words.html">Words</a></li>
        <li><a href="/people.html">People</a></li>
        <li><a href="/hashtags.html">Hashtags</a></li>
      </ul>
    </nav>
    <h1>Recipes</h1>
    <p><small>Embedded · MultiWord · JSON payload</small></p>
    <p>The payload of a hit — {category, minutes, emoji} — comes out of LUI with the text. …</p>
  </header>
  <main>
    <div id="scenario"></div>     <!-- the autocomplete, built by the client -->
    <section id="hits"></section> <!-- the hits of the last query, as a table -->
    <section id="envelope"></section> <!-- the request, the timings, the facts -->
    <section id="settings"></section> <!-- the settings of the query, as a form -->
    <section id="activity"></section> <!-- the requests of this page, as a table -->
  </main>
  <footer><hr><p><small>Nordmann.LUI — the sample of the library</small></p></footer>
</body>
</html>
```

**The index page** — the same shell, `data-view="hub"`, and instead of the six panels: the links to the six datasets and the
indexes panel (the six indexes with their state, the maintenance of each, and the fill of the three bulk ones).

**The files of the client:**

| The file | What it is |
|---|---|
| `wwwroot/css/mvp.css` | vendored, 10,264 B |
| `wwwroot/css/vendor/flexdatalist.css` | vendored, 6,211 B |
| `wwwroot/css/brand.css` | the brand's values — `:root` only, plus the two `@font-face` |
| `wwwroot/css/README.md` | the provenance of the three: source, version, licence, SHA-256 |
| `wwwroot/fonts/Montserrat-latin.woff2` | vendored, 37,956 B, SIL OFL 1.1 |
| `wwwroot/fonts/Montserrat-OFL.txt` | the licence of the font (SIL Open Font License 1.1) |
| `wwwroot/img/logo.svg`, `wwwroot/img/logo-white.svg` | the company's lockup (from `1.svg`), **cropped to its artwork** (`viewBox="478 802 2196 396"`), and the same file with the wordmark in white for the dark surface |
| `wwwroot/img/logo-short.svg`, `wwwroot/img/logo-short-white.svg` | the short lockup (from `2.svg`), cropped the same way, for the tight places — the footer, the images of the README |

**The modules** — one root module, unchanged in its duties:

| The module | After |
|---|---|
| `js/main.js` | reads `document.body.dataset` → the view and the dataset; mounts the panels the view needs; mounts the one scenario of the page |
| `js/core/page.js` | **new** — `pageConfig(dataset)` → `{ view, scenario }`; a pure function, so the node tests can check it without a browser |
| `js/panels/scenario-view.js` | one autocomplete, in MVP's vocabulary; no tab strip; no fill form (it moved to the index page) |
| `js/panels/hits-panel.js` | a `<table>` without a class |
| `js/panels/envelope-panel.js` | the request, the facts, and the timings as a table |
| `js/panels/settings-panel.js` | a `<form>`; the "?" is a `<details>` |
| `js/panels/activity-panel.js` | a `<table>` |
| `js/panels/indexes-panel.js` | a table of the six indexes with the maintenance and the fill |
| `js/panels/fill-form.js` | unchanged in its duty — it fills an index; it stands on the index page now |
| `js/scenarios/*.js` | unchanged — the scenarios are data and a presenter, and the page knows no index by name |
| `js/core/dom.js` | unchanged — `el()` builds the DOM from text, never from markup; **no module passes a `class` any more** |

## 6. The order: six pull requests

Each pull request is green on its own, and the sample works at every step of the series.

### PR 1 — The stylesheets of the client (the dependency and the guard)

- **Files:** `wwwroot/css/mvp.css` and `wwwroot/css/vendor/flexdatalist.css` (both vendored, byte for byte),
  `wwwroot/css/brand.css` (the brand's values), `wwwroot/css/README.md` (the provenance of the three: source, version,
  licence, SHA-256), `wwwroot/fonts/` (the two fonts and their two licences), `wwwroot/img/` (the company's lockup and its
  short variant, each cropped to its artwork and each with a white wordmark for the dark surface), and the guard
  `The_stylesheets_of_the_client_are_the_files_the_provenance_says` in `SampleClientTests.cs`.
- **The rule:** **the files are not linked yet.** The old page keeps its own stylesheets and looks exactly as it does today;
  the vendoring proves the provenance and the hash, and the link comes with the pages that use them (PR 2).
- **The trap:** the guard of the page's CSS (`The_page_has_no_inline_script_style_or_event_handler_and_loads_nothing_from_elsewhere`)
  walks every `.css` file in `wwwroot/css` and fails a foreign `@import` or a foreign `url()`. Both vendored files pass it:
  MVP.css carries one URL — in the header comment, which the guard's `url(\s*['"]?https?://` does not match — and the
  library's file carries none. The brand's layer must not carry a `url()` at all: the fonts are **not** referenced by a URL
  from the CSS but by a relative path in the `@font-face` (`url('../fonts/Montserrat-latin.woff2')`) — the guard's `ExternalUrl`
  matches only `http(s)://`, and the policy's `font-src` falls back to `default-src 'self'`, so a local file is served.
- **The acceptance:** the SHA-256 of the two vendored files is the one in the provenance; the guard is green; the brand's layer
  has no selector but `:root` (the guard of PR 5 says so); the sample is unchanged in the browser (the old page does not link
  the new files yet).

### PR 2 — The three curated datasets (recipes, tags, names)

- **Files:** `wwwroot/recipes.html`, `wwwroot/tags.html`, `wwwroot/names.html`, `js/main.js`, `js/core/page.js`,
  `js/panels/scenario-view.js`, the panels of a dataset page, the node tests, and the ADR-0070 (drafted).
- **The rule:** the page names its dataset and links the three stylesheets; the client mounts that one dataset. The old page
  stays the entry point with its own stylesheets — the hand-written `css/flexdatalist.css` included — until PR 4: the two
  worlds live side by side for two pull requests, no page links both dropdown stylesheets, and neither is broken.
- **The trap:** the nav of a new page links to pages that do not exist yet — the nav grows with the pages, so no link is
  ever broken. The fill form is still in the scenario view in this pull request (it moves in PR 4).
- **The acceptance:** each of the three pages shows its autocomplete, its hits, its envelope, its settings and its log; the
  dropdown opens under the input and is positioned there; the page carries the brand (the cobalt blue, the midnight navy,
  Montserrat); the browser check walks through the typo correction, the "Did you mean", the groups, the picker with its chips
  and its colour dot, the add-new, the abort while typing fast and a hostile entry; the node tests are green.

### PR 3 — The three bulk datasets (words, people, hashtags)

- **Files:** `wwwroot/words.html`, `wwwroot/people.html`, `wwwroot/hashtags.html`, the same client, the node tests.
- **The rule:** the same as PR 2 — the page of a dataset is the dataset, and the page of an empty index says that it is empty
  (the index exists only after the fill, ADR-0029).
- **The trap:** the three pages are empty until the fill — the page must say so and offer the fill, not show an empty input
  that never answers. The word list is downloaded on the first fill and the download takes a moment.
- **The acceptance:** the three pages show an empty index with the fill, the fill of the German word list runs from the page,
  its progress is watched, and the query stays in the milliseconds afterwards.

### PR 4 — The index page, the panel split and the old CSS (the hub)

- **Files:** `wwwroot/index.html` (the hub), the deletion of the old single page's markup and of `css/base.css`,
  `css/components.css`, `css/layout.css`, `css/tokens.css` and the hand-written `css/flexdatalist.css`, `js/main.js` (the
  hub view), `js/panels/indexes-panel.js`, `js/panels/fill-form.js`, the ADR-0070 (final), the notes in ADR-0027 and
  ADR-0029, the index of the ADRs, and the CHANGELOG.
- **The rule:** the indexes panel and the fill stand on the index page; the dataset pages carry the query (D5, D6). After this
  pull request `wwwroot/css/` holds **two files, one folder and a README** — `mvp.css`, `brand.css`, `vendor/flexdatalist.css` — and the
  sample writes no style of its own (D3).
- **The trap:** the two-column layout with the sticky settings panel is gone (ADR-0029, decision 9) — the settings sit in the
  flow of the page now, which is what the decision wanted (nothing lies over anything), but the decision itself has to be
  amended and not deleted. ADR-0027's decision 1 ("the sample writes its own stylesheet") changes with it: from now on the
  sample writes **no** style, and it vendors the one of the library.
- **The acceptance:** the index page lists the six datasets and administers the six indexes; the maintenance and the fill work
  from it; `wwwroot/css/` holds `mvp.css`, `brand.css`, `vendor/flexdatalist.css` and `README.md` and nothing else; the ADR-0070
  exists with its notes in ADR-0027 and ADR-0029, and the CHANGELOG has its entry.

### PR 5 — The guards

- **Files:** `tests/Nordmann.LUI.Architecture.Tests/SampleClientTests.cs` (three guards), the node test of `pageConfig`,
  `samples/Lui.Sample.Tests/SampleApiTests.cs` (the static files of the new pages), `docs/testing.md`.
- **The rule, in three guards:**
  1. **Every dataset has a page and every page names a dataset that exists** — the scenarios' ids against the `wwwroot/*.html`
     and their `data-scenario`. A page that names nothing must fail the build.
  2. **The client writes no style of its own** — every `.css` file under `wwwroot/css` is one of the three known files and its
     SHA-256 is the one in the provenance; and **the brand's layer has no selector but `:root`** (with or without an attribute)
     and no at-rule but `@font-face` or a `@media` around such a selector. A style of the sample's own fails the build. This is
     D3 and D10 in the form a machine can check.
  3. **The client's own modules set no class of their own** — no module of the sample passes a `class` to `el()`. The classes
     that exist on the page are the library's. The helper `core/dom.js` may still know the property; nothing may use it.
- **The trap:** a guard that is written after the code and never seen red proves nothing — each of the three is written first,
  and each is seen failing against the tree before the fix. A new test file needs its row in `docs/testing.md`, or the build
  fails (`TestLandscapeTests`).
- **The acceptance:** the three guards are red on the tree before the fix and green after it; the static files of the new pages
  are served with their media types; the map of the tests has the new rows.

### PR 6 — The documentation and the images

- **Files:** `README.md` (the sample section), `docs/README.md` (it says *two* files in `./images/` — there are three),
  `docs/images/sample.gif`, `docs/images/sample-recipes.png`, `docs/images/sample-tags.png`.
- **The rule:** the images show the page of today; a README that shows a page that no longer exists is a lie with a picture
  in it. The three files are replaced with the new pages.
- **The trap:** the film and the two screenshots are the work of Chromium — the browser check of ADR-0027 is by hand and
  Playwright is **not** a dependency of the repository. The images are captured outside the repository and checked in; the
  repository gains no dependency.
- **The acceptance:** the README shows the new pages, the images are of the new pages, `docs/README.md` names three files.

## 7. What it costs and what it does not do

| The change | It does | It does not do |
|---|---|---|
| The pages | one dataset per page, with the same search | it does not touch the API, the library or the server |
| The vocabulary | the page in MVP's own words | it does not turn the sample into a documentation page — the information stays, as a table and as strong text |
| The stylesheets | the layout from MVP, the dropdown from the library, the colours and the type from the brand | the sample writes **no** style — and the dropdown keeps the library's own look, tinted with the brand's values |
| The brand | the palette, the type, the logo, as far as the two vendored stylesheets allow | it does not give the page Gotham (commercial licence), the exclusion zone around the logo, or the brand's sunshine yellow for the `mark` — all three would need a style of our own |
| The loss | the tab strip, the two columns with the sticky settings, the card look, the timing bar as a bar, the colour dot as a dot | — |
| The bytes | 16,475 B of stylesheet instead of 15,001 B, plus 42 KB of the font and its licence | the two stylesheets are vendored: 10,264 B of MVP and 6,211 B of the library, both with a hash and a licence; the third file is the brand's values |

## 8. The traps that are known

- **The order of the stylesheets decides the ties.** MVP.css is linked first, then the library's file, then the brand's layer —
  a tie between two element rules is won by the later one, and the brand's layer must come after both, or its values would be
  the ones that lose.
- **`section` is a flex row in MVP** (mvp.css:87). A section that stacks content turns it into a row; the panels that stack
  are `article`s or `div`s. The old page's panels were `<section class="card">`.
- **`nav` has `margin-bottom: 7rem`** (mvp.css:148) — MVP's own header breathes like that. If it is judged too large on the
  pages of the sample, that is an ADR and a style of our own — which D3 forbids. That is the trade to see in the browser.
- **MVP forces light mode unless `<html color-mode="user">`** — every page needs the attribute, or the page is light in a dark
  system. The library's own file follows the system beside it, and the brand's layer gives both the same night.
- **The dropdown is the library's** — its list is placed by script and the stylesheet has to give it `position: absolute`
  (`flexdatalist.es.js:1415` sets `width`, `top`, `left` and never `position`). The library's own file does exactly that, and
  the sample writes none of it.
- **The properties of a row are the library's, and the library's file leaves them plain**: `.prop-label`, `.prop-emoji`,
  `.prop-meta` and `.prop-badge` are the names the library gives its spans (`flexdatalist.es.js:1307`), and its stylesheet
  does not style them — they read as one line of text. The sample's own four rules for them go with the stylesheet.
- **The colour dot needs a size, and an empty element has none.** It becomes a small image with the colour in its data URI
  (`img-src 'self' data:` is in the policy); the colour is validated as `#rrggbb` before it goes into the URI.
- **The guide's colours are at 100 % opacity** — every value of the brand's layer is opaque. MVP's own `--color-accent` is
  `#118bee15`, eight digits with an alpha, and the layer replaces it.
- **Gotham cannot go on a web page** — the guide names it for the headings, but it is a commercial licence (Hoefler &
  Frere-Jones). The guide's own logo wordmark is set in Montserrat ExtraBold, so Montserrat carries the headings. The
  deviation belongs in the ADR, not in a comment.
- **The tertiary typeface cannot come either.** Roboto Slab is the guide's face for *"captions, call-outs, sidebars, and data
  visualizations"* — and the sample's two tables are data visualisations. It would have to reach them through a
  `table { font-family: … }` rule, and that is a style of our own, which D3 forbids. So the tables carry Montserrat, and the
  font of Roboto Slab is not vendored. The deviation belongs in the ADR.
- **The brand has no dark palette** — the night is built from the brand's own dark values (the background Midnight Navy
  `#00002d`, the surface Black Coffee `#1a1a42`, the text Pure White). The library's own dark block must be overridden in the
  same media query: it is the same specificity, so the later rule in the cascade wins — which is why the brand's layer is
  linked last.
- **The logo's wordmark is `#333333`** — on the midnight navy it would be invisible. The rule the guide states for its own logo
  (*"the White version should be used for dark backgrounds"*) holds for the company's lockup for the same reason, so the header
  holds a `<picture>` with the two files and the browser chooses by the system. **No style is needed for that** — and the
  guide's colourway (`#017efa` / `#00002d` on page 14) is not the colourway of the attached files (`#267EC7` / `#333333`): a
  logo carries its colours and is not themed.
- **The attached logo files carry 86 % empty canvas** — the artwork is 2,196 × 396 inside 3,147 × 2,000 (and 3,220 × 2,000).
  Uncropped, the logo stands in the header as a small mark in a big empty box. The viewBox is cropped to the artwork
  (`viewBox="478 802 2196 396"`), with the clear space the guide asks for around it — and the lockup is **5.55 : 1**, so in the
  nav it is a wide strip (~160 × 29 px), not a square mark.
- **The CSP forbids a CDN** — and it forbids the Google Fonts CDN for the same reason: the fonts are vendored as files, with
  their licences (OFL 1.1 for Montserrat, Apache 2.0 for Roboto Slab), and `font-src` falls back to `default-src 'self'`.
- **MVP's own template has an external icon** (`mvp.html:7`, `https://via.placeholder.com/70x70`) — the pages of the sample
  must not carry it: the policy forbids it and the guard fails the build.
- **The images of the README show the old page** — until they are replaced, the entrance lies.
- **The browser check is not in CI** (ADR-0027) — what only a browser shows (the dropdown under the input, the policy, the
  dark mode, the brand on both surfaces, the layout of a page) is the acceptance of the pull requests that change the page,
  described in the pull request.
- **The map of the tests is a guard** — a new test file without a row in `docs/testing.md` fails the build.
- **The page is the first thing a reader sees** — the sample is the demonstration of the API surface (ADR-0027); a page per
  dataset makes it easier to read and easier to link, and it must not make the demonstration smaller: the whole surface stays.

## 9. What is not decided here

- **Which logo stands in the header.** The two attached files are the **Nordmann Development** logo (the company); the guide's
  logo is the **Timetables** mark and wordmark, and it exists only as vector inside the 25 MB PDF. Measured: the logo pages
  (PDF 20–21) carry **no raster image** — the whole guide has 48 of them and 39 of those sit in the mockups chapter (page 43)
  — and `pdftocairo -svg -f 21 -l 21` lifts the page out as 114 KB of SVG with 140 paths and 250 glyph uses (the wordmark is
  set in the embedded Montserrat ExtraBold). So the mark can be cropped out of the paths and the wordmark re-set in the
  Montserrat we vendor — **but the guide's logo is not used**: the owner's decision of 2026-11-16 is the **company's** lockup
  in the header (the sample is a page of the library, not a page of the product), and the guide gives the palette and the type.
- **Where the settings sit on a dataset page** — above the autocomplete or below the hits. The plan puts them below the
  results; the browser check decides, and it is a line in one module.
- **Whether the 7rem under the nav is acceptable** — MVP's default. If it is not, the change is an ADR and a style of our own
  — which D3 forbids.
- **Whether the fill of a bulk index stays on the index page** — the owner's answer says yes; ADR-0029 says it stands in the
  tab of the index it fills, and the note in ADR-0029 says why it moved.
- **The number of the ADR** — the next free one after ADR-0069 (0068 and 0069 are claimed by #171 and #172).
- **The numbers of the issues** — the epic and its issues are created when the plan is approved.

## 10. The record

| The decision | It lives in |
|---|---|
| D1 (the seven pages) | this plan; ADR-0070 |
| D2 (the two vendored stylesheets) | this plan; ADR-0027 (the vendored file and its provenance); ADR-0070 |
| D3 (no style of the sample's own) | this plan; ADR-0070; the guards of PR 5 |
| D4 (MVP's dark mode) | this plan; ADR-0070 |
| D5 (what is on which page) | this plan; ADR-0070 |
| D6 (the fill moves) | this plan; the note in ADR-0029 (decisions 9 and 10) |
| D7 (the page names its dataset) | this plan; ADR-0027 (the module tree and the policy) |
| D8 (the information stays, the way it is shown changes) | this plan; ADR-0070 |
| D9 (issue-driven, six pull requests) | this plan; AGENTS.md |
| D10 (the brand of the guide, and the company's logo in the header) | this plan; ADR-0070; the brand's layer and its provenance in `wwwroot/css/README.md`; the owner's answers of 2026-11-16 |
