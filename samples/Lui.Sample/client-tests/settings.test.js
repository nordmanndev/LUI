import assert from 'node:assert/strict';
import { test } from 'node:test';
import { createSettings, defaultSettings, toParams } from '../wwwroot/js/suggest/settings.js';

const libraryDefaults = { maxResults: 20, candidates: 500, rank: 'WordStartThenSimilarity', priorityMode: 'First', distinctText: false };
const defaults = defaultSettings(libraryDefaults);

test('the defaults are the library’s, not the page’s', () => {
    assert.equal(defaults.query.take, 20);
    assert.equal(defaults.query.candidates, 500);
    assert.equal(defaults.query.rank, 'WordStartThenSimilarity');
    assert.equal(defaults.query.similarity, '');
    assert.equal(defaults.query.typo, 'default');
});

test('only what differs from the library’s default is sent', () => {
    const settings = structuredClone(defaults);
    settings.query.timings = false;
    assert.deepEqual(toParams(settings, defaults), {});

    settings.query.take = 5;
    settings.query.rank = 'WeightOnly';
    settings.query.priorityMode = 'CandidatesOnly';
    settings.query.distinct = true;
    settings.query.similarity = 'ngram-dice';
    settings.query.candidates = 100;
    assert.deepEqual(toParams(settings, defaults), {
        take: '5', rank: 'WeightOnly', priorityMode: 'CandidatesOnly', distinct: 'true', similarity: 'ngram-dice', candidates: '100',
    });
});

test('the typo tolerance is three-valued: default sends nothing, on and off send a boolean', () => {
    const settings = structuredClone(defaults);
    settings.query.timings = false;
    assert.equal(toParams(settings, defaults).typo, undefined);
    settings.query.typo = 'on';
    assert.equal(toParams(settings, defaults).typo, 'true');
    settings.query.typo = 'off';
    assert.equal(toParams(settings, defaults).typo, 'false');
});

test('the demo latency is a parameter only when it is set', () => {
    const settings = structuredClone(defaults);
    assert.equal(toParams(settings, defaults).delayMs, undefined);
    settings.ui.delayMs = 700;
    assert.equal(toParams(settings, defaults).delayMs, '700');
});

test('the settings are kept in the storage and come back', () => {
    const store = new Map();
    const storage = { getItem: key => store.get(key) ?? null, setItem: (key, value) => store.set(key, value) };
    const changes = [];
    createSettings(defaults, storage, change => changes.push(change)).update({ query: { take: 3 }, ui: { minLength: 2 } });

    const again = createSettings(defaults, storage, () => {});
    assert.equal(again.get().query.take, 3);
    assert.equal(again.get().ui.minLength, 2);
    assert.equal(again.get().query.candidates, 500);
    assert.equal(changes.length, 1);
});

test('a storage that is blocked, broken or holds rubbish is not an error', () => {
    const broken = { getItem: () => { throw new Error('blocked'); }, setItem: () => { throw new Error('blocked'); } };
    const settings = createSettings(defaults, broken, () => {});
    settings.update({ query: { take: 9 } });
    assert.equal(settings.get().query.take, 9);

    const rubbish = { getItem: () => '{not json', setItem: () => {} };
    assert.equal(createSettings(defaults, rubbish, () => {}).get().query.take, 20);
    assert.equal(createSettings(defaults, null, () => {}).get().query.take, 20);
});

test('reset returns to the defaults and says so', () => {
    const changes = [];
    const settings = createSettings(defaults, null, change => changes.push(change));
    settings.update({ query: { take: 3 } });
    settings.reset();
    assert.equal(settings.get().query.take, 20);
    assert.equal(changes.length, 2);
});
