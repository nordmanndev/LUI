import assert from 'node:assert/strict';
import { test } from 'node:test';
import { fold } from '../wwwroot/js/suggest/fold.js';

test('the German transliteration comes before the fold, as in the search form', () => {
    assert.equal(fold('Müller Äpfel Öl Straße').folded, 'mueller aepfel oel strasse');
    assert.equal(fold('ẞ').folded, 'ss');
});

test('case and diacritics fold; what the tokenizer folds and Unicode does not is in the table', () => {
    assert.equal(fold('Café Crème').folded, 'cafe creme');
    assert.equal(fold('Łódź İsmail Ørsted').folded, 'lodz ismail orsted');
});

test('a decomposed umlaut is the umlaut', () => {
    const decomposed = 'Müller';
    assert.equal(fold(decomposed).folded, 'mueller');
});

test('every folded character knows the span of the original it came from', () => {
    const { folded, spans } = fold('Müller');
    assert.equal(folded.length, spans.length);
    // "ue" are both the one character "ü"
    assert.deepEqual(spans[1], [1, 2]);
    assert.deepEqual(spans[2], [1, 2]);
    // the decomposed form: the letter and the mark are one span
    assert.deepEqual(fold('Müller').spans[1], [1, 3]);
});

test('astral characters are not split', () => {
    const { folded, spans } = fold('a😀b');
    assert.equal(folded.length, spans.length);
    assert.equal(folded.at(-1), 'b');
});
