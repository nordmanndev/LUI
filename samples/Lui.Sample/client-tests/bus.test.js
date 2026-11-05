import assert from 'node:assert/strict';
import { test } from 'node:test';
import { createBus } from '../wwwroot/js/core/bus.js';

test('handlers get the detail, in order, and can leave', () => {
    const bus = createBus();
    const seen = [];
    const off = bus.on('a', detail => seen.push(['one', detail]));
    bus.on('a', detail => seen.push(['two', detail]));
    bus.emit('a', 1);
    off();
    bus.emit('a', 2);
    bus.emit('nobody');
    assert.deepEqual(seen, [['one', 1], ['two', 1], ['two', 2]]);
});

test('a handler that leaves while the event is delivered does not skip the others', () => {
    const bus = createBus();
    const seen = [];
    const off = bus.on('a', () => { off(); seen.push('first'); });
    bus.on('a', () => seen.push('second'));
    bus.emit('a');
    assert.deepEqual(seen, ['first', 'second']);
});

test('a handler that breaks does not take the others down with it', () => {
    // One panel that cannot read its data must not make the hits of a query vanish: the panels are independent.
    const bus = createBus();
    const seen = [];
    bus.on('a', () => { throw new Error('the index panel could not be read'); });
    bus.on('a', () => seen.push('the hits are still there'));
    bus.emit('a');
    assert.deepEqual(seen, ['the hits are still there']);
});
