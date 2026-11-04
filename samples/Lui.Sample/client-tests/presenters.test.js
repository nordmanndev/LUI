import assert from 'node:assert/strict';
import { test } from 'node:test';
import { scenarios } from '../wwwroot/js/scenarios/index.js';

/**
 * flexdatalist writes every displayed property with innerHTML, and the raw value if the item has no `<property>_highlight`.
 * So: for every scenario, with hostile data in every field a hit has, every property that is displayed has an escaped twin,
 * and the twin holds no markup but the three things the page makes itself.
 */
const HOSTILE = '<img src=x onerror="window.__pwned=1">';
const hit = {
    key: 7,
    text: HOSTILE,
    weight: 1,
    priority: 'System',
    similarity: 0.9,
    atWordStart: true,
    payload: { category: HOSTILE, minutes: HOSTILE, emoji: HOSTILE, kind: HOSTILE, color: `#12345"><svg onload=1>` },
};
const envelope = { query: 'img', correctedQuery: null, results: [hit] };

/** What the page itself puts into the HTML: the match, and the colour dot (whose colour is validated). */
const ours = html => html
    .replace(/<span class="swatch" data-color="#[0-9a-fA-F]{6}"><\/span>/g, '')
    .replaceAll('<span class="highlight">', '')
    .replaceAll('</span>', '');

for (const scenario of scenarios) {
    test(`${scenario.id}: every displayed property has an escaped twin, and no hostile markup survives`, () => {
        const [item] = scenario.presenter({ ...envelope, results: [hit] });
        const { visibleProperties, groupBy } = scenario.flex({ grouped: true });

        const displayed = [...visibleProperties, groupBy].filter(Boolean);
        for (const property of displayed) {
            if (item[property] === undefined) {
                continue; // flexdatalist skips a property the item does not have
            }
            const twin = item[`${property}_highlight`];
            assert.equal(typeof twin, 'string', `${scenario.id}.${property} has no _highlight twin`);
            assert.ok(!ours(twin).includes('<'), `${scenario.id}.${property}: ${twin}`);
        }
        assert.ok(displayed.includes('label'), 'the text is displayed');
    });

    test(`${scenario.id}: the presenter is pure data — no DOM, one item per hit, in order`, () => {
        const many = { query: 'a', results: [1, 2, 3].map(key => ({ ...hit, key, text: `text ${key}`, priority: 'Normal' })) };
        assert.deepEqual(scenario.presenter(many).map(item => item.key), [1, 2, 3]);
    });

    test(`${scenario.id}: a hit without a payload does not break the presenter`, () => {
        const [item] = scenario.presenter({ query: 'a', results: [{ ...hit, text: 'plain', payload: undefined }] });
        assert.equal(item.label, 'plain');
    });
}

test('a badge shows the priority of a hit above normal, and only then', () => {
    const [system, normal] = scenarios[0].presenter({ query: 'a', results: [{ ...hit, text: 'a' }, { ...hit, text: 'b', priority: 'Normal' }] });
    assert.equal(system.badge, 'System');
    assert.equal(normal.badge, undefined);
});

test('the colour of a tag is used only when it is #rrggbb', () => {
    const tags = scenarios.find(scenario => scenario.id === 'tags');
    const [good, bad] = tags.presenter({
        query: 'a',
        results: [
            { ...hit, text: 'a', payload: { kind: 'k', color: '#2f855a' } },
            { ...hit, text: 'b', payload: { kind: 'k', color: 'red' } },
        ],
    });
    assert.match(good.swatch_highlight, /data-color="#2f855a"/);
    assert.match(bad.swatch_highlight, /data-color="#718096"/);
});
