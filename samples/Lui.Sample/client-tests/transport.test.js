import assert from 'node:assert/strict';
import { test } from 'node:test';
import { createBus } from '../wwwroot/js/core/bus.js';
import { createSuggestTransport } from '../wwwroot/js/net/suggest-transport.js';

const envelope = (text = 'a') => ({ query: text, results: [{ key: 1, text }] });
const present = e => e.results.map(hit => ({ key: hit.key, label: hit.text }));

/** A fetch that answers when the test says so, and ends with an AbortError when its signal is aborted — as the real one does. */
function controllableFetch() {
    const calls = [];
    const fetchImpl = (url, init = {}) => new Promise((resolve, reject) => {
        const call = { url, resolve: body => resolve(new Response(JSON.stringify(body), { status: 200 })), reject, signal: init.signal };
        init.signal?.addEventListener('abort', () => reject(new DOMException('aborted', 'AbortError')));
        calls.push(call);
    });
    return { fetchImpl, calls };
}

function setup(overrides = {}) {
    const bus = createBus();
    const events = [];
    for (const type of ['suggest:request', 'suggest:response', 'suggest:aborted', 'suggest:error']) {
        bus.on(type, detail => events.push([type, detail]));
    }
    const { fetchImpl, calls } = overrides.fetchImpl ? { fetchImpl: overrides.fetchImpl, calls: [] } : controllableFetch();
    const transport = createSuggestTransport({
        fetchImpl,
        bus,
        presenters: new Map([['recipes', present]]),
        base: 'http://localhost',
        now: (() => { let t = 0; return () => (t += 5); })(),
    });
    return { transport, calls, events };
}

const names = events => events.map(([type]) => type);

test('a request that is not a suggestion goes on untouched', async () => {
    const seen = [];
    const { transport, events } = setup({ fetchImpl: async (url, init) => { seen.push([url, init]); return new Response('{}'); } });
    await transport.fetch('/api/indexes', { method: 'GET' });
    assert.deepEqual(seen, [['/api/indexes', { method: 'GET' }]]);
    assert.deepEqual(events, []);
});

test('the response is the presenter’s items, and the whole envelope goes to the bus', async () => {
    const { transport, calls, events } = setup();
    const pending = transport.fetch('/api/suggest/recipes?q=apfel&timings=true');
    calls[0].resolve({ ...envelope('apfel'), correctedQuery: 'apfel', timings: { totalMs: 1 } });
    const response = await pending;

    assert.deepEqual(await response.json(), { results: [{ key: 1, label: 'apfel' }] });
    assert.deepEqual(names(events), ['suggest:request', 'suggest:response']);
    const [, detail] = events[1];
    assert.equal(detail.index, 'recipes');
    assert.deepEqual(detail.request, { q: 'apfel', timings: 'true' });
    assert.equal(detail.envelope.correctedQuery, 'apfel');
    assert.equal(detail.roundtripMs, 5);
});

test('a new request for an index aborts the one in flight; the stale answer is never rendered', { timeout: 1000 }, async () => {
    const { transport, calls, events } = setup();
    const first = transport.fetch('/api/suggest/recipes?q=a');
    const second = transport.fetch('/api/suggest/recipes?q=ap');

    await assert.rejects(first, { name: 'AbortError' });
    assert.equal(calls[0].signal.aborted, true);
    assert.equal(calls[1].signal.aborted, false);

    calls[1].resolve(envelope('ap'));
    assert.deepEqual((await (await second).json()).results, [{ key: 1, label: 'ap' }]);
    assert.deepEqual(names(events), ['suggest:request', 'suggest:request', 'suggest:aborted', 'suggest:response']);
});

test('cancel(index) aborts the request in flight — on a keystroke, before the debounce has fired', { timeout: 1000 }, async () => {
    const { transport, calls, events } = setup();
    const pending = transport.fetch('/api/suggest/recipes?q=a');
    transport.cancel('recipes');
    await assert.rejects(pending, { name: 'AbortError' });
    assert.equal(calls[0].signal.aborted, true);
    assert.ok(names(events).includes('suggest:aborted'));

    // nothing in flight: nothing happens
    transport.cancel('recipes');
    transport.cancel('unknown');
});

test('the indexes do not abort each other', async () => {
    const { transport, calls } = setup();
    const recipes = transport.fetch('/api/suggest/recipes?q=a');
    const tags = transport.fetch('/api/suggest/tags?q=a');
    assert.equal(calls[0].signal.aborted, false);
    calls[0].resolve(envelope());
    calls[1].resolve(envelope());
    await Promise.all([recipes, tags]);
});

test('a refused request is an empty list for flexdatalist and a reason for the bus', async () => {
    const { transport, events } = setup({
        fetchImpl: async () => new Response(JSON.stringify({ title: 'Bad request', detail: "rank 'x' is not one of: …" }), { status: 400 }),
    });
    const response = await transport.fetch('/api/suggest/recipes?q=a&rank=x');
    assert.deepEqual(await response.json(), { results: [] });
    assert.equal(events.at(-1)[0], 'suggest:error');
    assert.equal(events.at(-1)[1].status, 400);
    assert.match(events.at(-1)[1].detail, /not one of/);
});

test('a network error is an empty list too, not an exception inside the library', async () => {
    const { transport, events } = setup({ fetchImpl: async () => { throw new TypeError('Failed to fetch'); } });
    const response = await transport.fetch('/api/suggest/recipes?q=a');
    assert.deepEqual(await response.json(), { results: [] });
    assert.deepEqual(events.at(-1), ['suggest:error', { index: 'recipes', status: 0, detail: 'Failed to fetch' }]);
});

test('an index without a presenter gets the results as they are', async () => {
    const { transport, calls } = setup();
    const pending = transport.fetch('/api/suggest/other?q=a');
    calls[0].resolve({ query: 'a', results: [{ x: 1 }] });
    assert.deepEqual((await (await pending).json()).results, [{ x: 1 }]);
});
