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
