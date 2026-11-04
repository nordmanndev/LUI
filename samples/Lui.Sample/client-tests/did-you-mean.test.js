import assert from 'node:assert/strict';
import { test } from 'node:test';
import { correctionOf } from '../wwwroot/js/suggest/did-you-mean.js';

test('a correction is what LUI searched when it differs from what was typed', () => {
    assert.equal(correctionOf({ query: 'Apfle', correctedQuery: 'apfel' }), 'apfel');
    assert.equal(correctionOf({ query: 'kartofel', correctedQuery: 'kartoffel' }), 'kartoffel');
});

test('there is nothing to ask when LUI did not correct, or corrected to what was typed', () => {
    assert.equal(correctionOf({ query: 'apfel' }), null);
    assert.equal(correctionOf({ query: 'apfel', correctedQuery: null }), null);
    assert.equal(correctionOf({ query: 'apfel', correctedQuery: '' }), null);
    assert.equal(correctionOf({ query: ' Apfel ', correctedQuery: 'apfel' }), null);
    assert.equal(correctionOf(null), null);
    assert.equal(correctionOf(undefined), null);
});
