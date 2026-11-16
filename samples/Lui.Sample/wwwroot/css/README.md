# The stylesheets, the fonts and the images of the client

Every file of the client that comes from somewhere else is named here with its source, its version, its licence and its
SHA-256. The guard `The_stylesheets_of_the_client_are_the_files_the_provenance_says` holds each of them to this table: a file
that is not the one this page describes fails the build.

**The paths are relative to `wwwroot/`.** The hashes are taken over the file as the repository has it — the line endings
normalised back to LF for the text files, the bytes as they are for the font.

| File | Version | Source | Licence | SHA-256 |
|---|---|---|---|---|
| `css/mvp.css` | 1.18 | [`https://unpkg.com/mvp.css`](https://unpkg.com/mvp.css) — the file the site of the author serves | MIT | `e0843dbd4883d1d30ef62102ff498a1698deab8d435f889e2b2be0288a45cccd` |
| `css/vendor/flexdatalist.css` | 3.1.1 | [`https://unpkg.com/flexdatalist@3.1.1/dist/flexdatalist.css`](https://unpkg.com/flexdatalist@3.1.1/dist/flexdatalist.css) | MIT | `449b37f0e0a83b15aa52939593534d8f60bd1227abf9b17ade1db0ef28527e70` |
| `fonts/Montserrat-latin.woff2` | 31 | the Latin subset of the variable font of [Google Fonts](https://fonts.google.com/specimen/Montserrat) | SIL Open Font License 1.1 (`fonts/Montserrat-OFL.txt`) | `06b16db7a969135d48d38c49183be7fb88d4452e2a3011957c7851941f4e4879` |
| `img/logo.svg` | — | the lockup of Nordmann Development, cropped to its artwork | the file of the owner | `a02e49286fcf7d005e16bf2b204477cc33398c92ecc50b31129da2e86402931b` |
| `img/logo-white.svg` | — | the same file with the wordmark in white — the version for a dark background | the file of the owner | `49fec810c0e57e0133865ee44c9b8fdadd93f550fe96e6acee3eb0fb2f79ffb1` |
| `img/logo-short.svg` | — | the short lockup of Nordmann Development, cropped to its artwork | the file of the owner | `93080716c26660511f7979700a967f4f05730f0fcf2556cc5b8edd9969718891` |
| `img/logo-short-white.svg` | — | the same file with the wordmark in white | the file of the owner | `f9fc3e64a7941a32d342ff6588ce6da366e2b875beadf0210819d350824ad663` |

## The two stylesheets

**`css/mvp.css`** is MVP.css by Andy Brewer — *"A minimalist stylesheet for HTML elements"*, MIT. It styles the root elements
of a document and **has no class selector at all** (135 selectors, every one of them on an element). It is the layout of the
client: the typography, the forms, the tables, the navigation, the dark mode. Its own words: *"Most styles are editable
through CSS variables"* — and `css/brand.css` is what edits them.

**`css/vendor/flexdatalist.css`** is the stylesheet that **flexdatalist itself ships** (3.1.1) — the same version as the
vendored `js/vendor/flexdatalist.es.js`, and the two belong together. It is what makes the dropdown work without a line of CSS
from the sample: flexdatalist places its list by script (`flexdatalist.es.js:1415` sets `width`, `top` and `left`) and **never
sets `position`**, so the stylesheet has to give `.flexdatalist-results` its `position: absolute`. The library also styles the
chips of the picker, the group header, the active row and the loading line, and it has its own palette (`--fdl-accent`,
`--fdl-bg`, `--fdl-border`, `--fdl-text`, `--fdl-tag-bg`, `--fdl-tag-border`, `--fdl-tag-fg`, `--fdl-group-bg`,
`--fdl-group-border`, `--fdl-shadow`, `--fdl-radius`, `--fdl-accent-light`, `--fdl-accent-fg`, `--fdl-text-muted`) — the
brand's values take the place of its own in `css/brand.css`.

The file lives in `css/vendor/` and not next to `css/mvp.css`, because the sample still has its own `css/flexdatalist.css`
until the pull request that deletes the stylesheets of the sample.

## The font

**`fonts/Montserrat-latin.woff2`** is the Latin subset of the variable font of Montserrat (37,956 B, one file for every
weight from 100 to 900), under the SIL Open Font License 1.1 — the licence is in `fonts/Montserrat-OFL.txt` beside it.
Montserrat is the primary typeface of the brand guide. The guide's secondary typeface (Gotham) is a commercial licence and
cannot go on a web page; its tertiary typeface (Roboto Slab) would need a rule of its own, and the sample writes no style.

The font is vendored because the policy of the sample forbids a foreign origin (`style-src 'self'`), and the fonts are files
like every other asset of the client.

## The images

The four images are the lockup of Nordmann Development, **cropped to its artwork**: the file of the owner carries 86 % empty
canvas (the artwork is 2,196 × 396 inside 3,147 × 2,000), and uncropped it would stand in the header as a small mark in a big
empty box. The `-white` variants carry the wordmark in white — the rule of the brand guide (*"the White version should be used
for dark backgrounds"*) holds for this lockup for the same reason: its wordmark is `#333333`, and that is invisible on the navy
of the dark mode. The page holds both in a `<picture>`, and the browser picks by the system — no rule of our own is needed.
