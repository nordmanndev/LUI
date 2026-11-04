import { escapeHtml } from '../core/dom.js';
import { fold } from './fold.js';

/**
 * Marks where a hit matches the input. flexdatalist marks only in its own client-side filter, which `searchDisabled`
 * switches off (the server ranks, corrects typos and transliterates; a client filter would drop the corrected hits). So the
 * page produces what the library would have: `<property>_highlight`, a string of HTML with `<span class="highlight">`.
 *
 * Which words: the *corrected* query when LUI corrected, the query otherwise — `kartofel` → `kartoffel` is what lights up.
 * Where: a word start first (the match LUI ranks first), else anywhere (the trigram index finds `kuchen` in `Apfelkuchen`).
 */

const SEPARATORS = /[\s\-_.,;:/()#&+]+/u;
const isSeparator = character => character === undefined || SEPARATORS.test(character);

/**
 * @param {string} query
 * @returns {string[]} the folded words of the input, longest first
 */
export function queryWords(query) {
    return fold(query).folded.split(SEPARATORS).filter(Boolean).sort((a, b) => b.length - a.length);
}

/**
 * @param {string} folded
 * @param {string} word
 * @param {boolean[]} taken
 * @param {boolean} atWordStart
 * @returns {number|null}
 */
function find(folded, word, taken, atWordStart) {
    for (let from = folded.indexOf(word); from !== -1; from = folded.indexOf(word, from + 1)) {
        const startsWord = from === 0 || isSeparator(folded[from - 1]);
        const free = taken.slice(from, from + word.length).every(used => !used);
        if (free && (!atWordStart || startsWord)) {
            return from;
        }
    }
    return null;
}

/**
 * @param {string} text the original text of the hit
 * @param {string} query what was searched
 * @returns {Array<[number, number]>} `[start, end)` in `text`, sorted, not overlapping
 */
export function highlightRanges(text, query) {
    const { folded, spans } = fold(text);
    const taken = new Array(folded.length).fill(false);

    for (const word of queryWords(query)) {
        const at = find(folded, word, taken, true) ?? find(folded, word, taken, false);
        if (at !== null) {
            taken.fill(true, at, at + word.length);
        }
    }

    /** @type {Array<[number, number]>} */
    const ranges = [];
    for (let i = 0; i < taken.length; i++) {
        if (!taken[i]) {
            continue;
        }
        const [start, end] = spans[i];
        const last = ranges.at(-1);
        if (last && start <= last[1]) {
            last[1] = Math.max(last[1], end);
        } else {
            ranges.push([start, end]);
        }
    }
    return ranges;
}

/**
 * The text as HTML for flexdatalist: **every character escaped**, the matches wrapped in the library's own class.
 *
 * @param {string} text
 * @param {Array<[number, number]>} ranges
 * @returns {string}
 */
export function toHighlightHtml(text, ranges) {
    let html = '';
    let from = 0;
    for (const [start, end] of ranges) {
        html += escapeHtml(text.slice(from, start));
        html += `<span class="highlight">${escapeHtml(text.slice(start, end))}</span>`;
        from = end;
    }
    return html + escapeHtml(text.slice(from));
}

/**
 * @param {string} text
 * @param {string} query
 * @returns {string}
 */
export function highlight(text, query) {
    return toHighlightHtml(text, highlightRanges(text, query));
}
