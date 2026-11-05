import assert from 'node:assert/strict';
import { test } from 'node:test';
import { fillProgress } from '../wwwroot/js/core/format.js';

test('the progress of a fill says what it is doing', () => {
    assert.equal(fillProgress(null), 'Not filled yet.');
    assert.equal(
        fillProgress({ state: 'running', requested: 10000, read: 2500, batches: 3 }),
        'Filling … 2,500 of 10,000 entries, 3 batches.',
    );
    assert.equal(
        fillProgress({ state: 'running', requested: 10000, read: 0, batches: 0, note: 'downloading the German word list …' }),
        'downloading the German word list …',
    );
    assert.equal(
        fillProgress({ state: 'done', requested: 10000, read: 10000, added: 10000, removed: 0, elapsedMs: 1234.5 }),
        'Filled: 10,000 entries — 10,000 added, 0 removed, 1.2 s.',
    );
    assert.equal(fillProgress({ state: 'cancelled', requested: 10000, read: 400 }), 'Cancelled after 400 entries.');
    assert.equal(fillProgress({ state: 'failed', error: 'no word list' }), 'Failed: no word list.');
    assert.equal(fillProgress({ state: 'failed' }), 'Failed: unknown reason.');
});
