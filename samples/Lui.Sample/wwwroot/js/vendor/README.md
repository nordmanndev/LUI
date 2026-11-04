# Vendored: flexdatalist

| | |
|---|---|
| Package | [`flexdatalist`](https://www.npmjs.com/package/flexdatalist) **3.1.1** — "Standalone autocomplete/datalist input — no dependencies" |
| Source | https://github.com/sergiodlopes/flexdatalist (`packages/core`) |
| File | `flexdatalist.es.js` — the **ES module build** (`dist/flexdatalist.es.js` of the npm package), **unmodified** |
| SHA-256 | `bc779389106ad840db94891bfd3d8f1e1e31b8198ac8746deafd6ad1f1806d82` |
| Licence | MIT, © 2016–2026 Sérgio Dinis Lopes (the header of the file) |
| CSS | **not vendored.** The sample writes its own (`css/flexdatalist.css`) for the class names the library uses. |

The file is checked by the architecture test `Sample_Client`: its hash must be the one above. To update, download the new version
from npm, replace the file **and** the hash and version here, read the changes in `src/flexdatalist.js` (the sample depends on
how it highlights, caches and requests — ADR-0027), and run the browser check.

What the sample relies on (read in the source of 3.1.1):

- `searchDisabled: true` skips the library's own filter, so the server's ranking, its typo correction and its transliteration
  stay in charge; nothing is highlighted then — the sample supplies `<property>_highlight`.
- The values of `visibleProperties` and the group header are written with `innerHTML`; without a `<property>_highlight` the
  **raw** value is. The sample gives every displayed property an escaped twin.
- `cache` is keyed by keyword and relatives, not by `params`: it is switched off.
- No request is aborted; at most one is kept pending while one is in flight. The sample aborts in its fetch boundary.
