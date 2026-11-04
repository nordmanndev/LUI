/**
 * Folds a text the way LUI's search form is made (docs/normalization.md, §1): the German transliteration, then what the
 * tokenizer of the search index makes of every code point — and remembers, for every character of the result, where it came
 * from. The page needs it to mark a match in the *original* text: `Müller` is `mueller` in the search form, so `mueller` has
 * to light up `Müller`; all five letters of the folded form map back to a span of the original.
 *
 * The folding is **exact**, not guessed: it is the table LUI measured from SQLite's tokenizer for every code point of the
 * Basic Multilingual Plane (`fold-table.js`, generated from `tokenizer-fold.txt`). That is why it is not specific to a language:
 * `č ć š ž` fold to `c c s z`, and `đ ł ø ħ ı` stay what they are, because the tokenizer leaves them — and LUI follows it.
 * What stays approximate is *which* part matched (see `highlight.js`), not how a letter folds.
 *
 * LUI has no `Highlight` yet (spec §9.3, issue #20). When it delivers ranges, this file and `highlight.js` go away.
 */

import { TOKENIZER_FOLD } from './fold-table.js';

/** The German transliteration of LUI (an option of the index, on by default): it runs before the tokenizer's folding. */
const TRANSLITERATION = new Map([
    ['ä', 'ae'], ['ö', 'oe'], ['ü', 'ue'], ['ß', 'ss'],
    ['Ä', 'ae'], ['Ö', 'oe'], ['Ü', 'ue'], ['ẞ', 'ss'],
]);

const DIAERESIS = '\u0308';
const UMLAUT_BASES = new Map([['a', 'ae'], ['o', 'oe'], ['u', 'ue'], ['A', 'ae'], ['O', 'oe'], ['U', 'ue']]);
const BASIC_MULTILINGUAL_PLANE = 0xffff;

/**
 * @param {string} character one code point
 * @returns {string}
 */
function foldCharacter(character) {
    const transliterated = TRANSLITERATION.get(character);
    if (transliterated !== undefined) {
        return transliterated;
    }

    const codePoint = character.codePointAt(0);
    // beyond the Basic Multilingual Plane the table has no entry: the tokenizer passes it through, so do we
    return codePoint > BASIC_MULTILINGUAL_PLANE ? character : (TOKENIZER_FOLD.get(codePoint) ?? character);
}

/**
 * @param {string} text
 * @returns {{ folded: string, spans: Array<[number, number]> }} `spans[i]` is the `[start, end)` of the original that
 *   produced `folded[i]` (UTF-16 units). A mark the tokenizer drops belongs to the character before it, so a span never
 *   ends between a letter and its accent.
 */
export function fold(text) {
    let folded = '';
    /** @type {Array<[number, number]>} */
    const spans = [];

    const push = (chars, start, end) => {
        if (chars === '' && spans.length > 0) {
            // a dropped mark: the character before it takes it along
            const first = spans.at(-1)[0];
            for (let k = spans.length - 1; k >= 0 && spans[k][0] === first; k--) {
                spans[k][1] = end;
            }
            return;
        }
        for (const unit of chars) {
            folded += unit;
            for (let k = 0; k < unit.length; k++) {
                spans.push([start, end]);
            }
        }
    };

    for (let i = 0; i < text.length;) {
        const character = String.fromCodePoint(text.codePointAt(i));
        const next = i + character.length;

        // a decomposed umlaut — a letter and U+0308 — is the umlaut (the C# side does the same)
        if (UMLAUT_BASES.has(character) && text[next] === DIAERESIS) {
            push(UMLAUT_BASES.get(character), i, next + 1);
            i = next + 1;
        } else {
            push(foldCharacter(character), i, next);
            i = next;
        }
    }

    return { folded, spans };
}
