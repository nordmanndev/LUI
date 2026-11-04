/**
 * Folds a text the way LUI's search form is made (docs/normalization.md, §1) — German transliteration, then case and
 * diacritics — and remembers, for every character of the result, where it came from. The page needs it to mark a match
 * in the *original* text: `Müller` is `mueller` in the search form, so `mueller` has to light up `Müller`, all five
 * letters of the folded form map back to a span of the original.
 *
 * This is an approximation, on purpose: LUI answers with the hits and not with ranges yet (spec §9.3, issue #20), and the
 * tokenizer's own folding is per character with a few surprises (`ί` stays, `ĳ` stays). When LUI delivers `Highlight`, this
 * file and `highlight.js` are what goes away.
 */

const TRANSLITERATION = new Map([
    ['ä', 'ae'], ['ö', 'oe'], ['ü', 'ue'], ['ß', 'ss'],
    ['Ä', 'ae'], ['Ö', 'oe'], ['Ü', 'ue'], ['ẞ', 'ss'],
]);

/** What the tokenizer folds that Unicode's decomposition does not. */
const EXTRA = new Map([
    ['ł', 'l'], ['Ł', 'l'], ['ø', 'o'], ['Ø', 'o'], ['đ', 'd'], ['Đ', 'd'], ['ħ', 'h'], ['Ħ', 'h'], ['ı', 'i'],
]);

const COMBINING_MARKS = /[̀-ͯ]/g;
const DIAERESIS = '̈';
const UMLAUT_BASES = new Map([['a', 'ae'], ['o', 'oe'], ['u', 'ue'], ['A', 'ae'], ['O', 'oe'], ['U', 'ue']]);

/**
 * @param {string} character one code point
 * @returns {string}
 */
function foldCharacter(character) {
    return TRANSLITERATION.get(character)
        ?? EXTRA.get(character)
        ?? character.normalize('NFD').replace(COMBINING_MARKS, '').toLowerCase().replaceAll('̇', '');
}

/**
 * @param {string} text
 * @returns {{ folded: string, spans: Array<[number, number]> }} `spans[i]` is the `[start, end)` of the original that
 *   produced `folded[i]` (UTF-16 units)
 */
export function fold(text) {
    let folded = '';
    /** @type {Array<[number, number]>} */
    const spans = [];

    const push = (chars, start, end) => {
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
