import assert from 'node:assert/strict';
import { test } from 'node:test';
import { fold } from '../wwwroot/js/suggest/fold.js';

test('the German transliteration comes before the fold, as in the search form', () => {
    assert.equal(fold('Müller Äpfel Öl Straße').folded, 'mueller aepfel oel strasse');
    assert.equal(fold('ẞ').folded, 'ss');
});

test('case and diacritics fold as the tokenizer folds them', () => {
    assert.equal(fold('Café Crème').folded, 'cafe creme');
    assert.equal(fold('İsmail').folded, 'ismail');
});

test('what the tokenizer leaves alone, the fold leaves alone: đ ł ø ħ ı are not folded to d l o h i', () => {
    // measured, not assumed (tokenizer-fold.txt): the search index does not find "Đakovo" by "dakovo", so the page must not mark it
    assert.equal(fold('Đakovo Łódź Ørsted Ħamrun ısı').folded, 'đakovo łodz ørsted ħamrun ısı');
});

test('Croatian: č ć š ž fold to c c s z', () => {
    assert.equal(fold('Čevapčići Ćosić Šibenik Žumberak').folded, 'cevapcici cosic sibenik zumberak');
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

test('a mark the tokenizer drops belongs to the letter before it: a span never ends between a letter and its accent', () => {
    const { folded, spans } = fold('e\u0301x');
    assert.equal(folded, 'ex');
    assert.deepEqual(spans[0], [0, 2]);
    assert.deepEqual(spans[1], [2, 3]);
});

test('astral characters are not split', () => {
    const { folded, spans } = fold('a😀b');
    assert.equal(folded.length, spans.length);
    assert.equal(folded.at(-1), 'b');
});
