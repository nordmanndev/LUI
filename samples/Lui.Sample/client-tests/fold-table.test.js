import assert from 'node:assert/strict';
import fs from 'node:fs';
import { test } from 'node:test';
import { fileURLToPath } from 'node:url';
import { TOKENIZER_FOLD } from '../wwwroot/js/suggest/fold-table.js';
import { fold } from '../wwwroot/js/suggest/fold.js';

/**
 * The page folds like the search index because it uses the table LUI measured from SQLite's tokenizer (the C# normalizer is
 * held to the same table by its parity test). This test keeps the JS copy and the measured file in step.
 */
const MEASURED = fileURLToPath(new URL('../../../src/Nordmann.LUI/Normalization/tokenizer-fold.txt', import.meta.url));

const parse = () => new Map(fs.readFileSync(MEASURED, 'utf8')
    .split('\n')
    .filter(line => line && !line.startsWith('#'))
    .map(line => {
        const [from, to] = line.split(' ');
        return [Number.parseInt(from, 16), to === '-' ? '' : String.fromCodePoint(Number.parseInt(to, 16))];
    }));

test('the generated table is the measured table', () => {
    const measured = parse();
    assert.equal(TOKENIZER_FOLD.size, measured.size, 'run: node tools/generate-fold-table.mjs');
    for (const [codePoint, folded] of measured) {
        assert.equal(TOKENIZER_FOLD.get(codePoint), folded, `U+${codePoint.toString(16).toUpperCase()} — run: node tools/generate-fold-table.mjs`);
    }
});

test('every code point of the Basic Multilingual Plane folds as the measured table says, apart from the German transliteration', () => {
    const measured = parse();
    const transliterated = new Set(['ä', 'ö', 'ü', 'ß', 'Ä', 'Ö', 'Ü', 'ẞ']);
    for (let codePoint = 0; codePoint <= 0xffff; codePoint++) {
        if (codePoint >= 0xd800 && codePoint <= 0xdfff) {
            continue; // a surrogate is half of a character
        }
        const character = String.fromCodePoint(codePoint);
        if (transliterated.has(character)) {
            continue;
        }
        const expected = measured.get(codePoint) ?? character;
        const { folded } = fold(character);
        assert.equal(folded, expected, `U+${codePoint.toString(16).toUpperCase().padStart(4, '0')}`);
    }
});

test('the five letters that are not folded are the ones the tokenizer leaves alone', () => {
    for (const letter of 'đłøħı') {
        assert.equal(TOKENIZER_FOLD.has(letter.codePointAt(0)), false, letter);
    }
});
