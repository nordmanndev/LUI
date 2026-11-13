import { api } from '../net/api.js';
import { clear, el } from '../core/dom.js';
import { number } from '../core/format.js';

/**
 * The rest of the public surface of LUI, one index at a time: what an index is, what the application writes to it, and how it
 * is kept in order. The suggest route is the other panels; this is ADR-0016 – ADR-0017, ADR-0034 of the specification.
 *
 *  - **embedded** (`recipes`, `names`): the entries are LUI's — `Upsert`, `UpdateWeight`, `UpdatePriority`, `Remove`, and
 *    `Sync` against the seed;
 *  - **linked** (`tags`): the text is the application's table — the page writes the row, and tells the index (the triggers
 *    and the outbox, `Refresh`, `ApplyPending`).
 *
 * Every index has a tab of its own: one card at a time, not six cards in a column. The tab of a bulk index (`words`,
 * `people`, `hashtags`) holds the button that creates the index and loads it (ADR-0029).
 *
 * @param {{ root: HTMLElement, bus: ReturnType<typeof import('../core/bus.js').createBus>, meta: any }} options
 */
export function mountIndexesPanel({ root, bus, meta }) {
    const tabs = el('div', { class: 'tabs index-tabs', role: 'tablist', 'aria-label': 'The indexes' });
    const host = el('div', { class: 'index-host' });
    const notice = el('p', { class: 'error', hidden: true });
    /** What the page remembers across a redraw of the card: the last output and which tab is open. */
    const outputs = new Map();
    let selected = null;
    root.append(el('h2', { text: 'The indexes' }), tabs, host, notice);

    async function refresh() {
        let indexes;
        try {
            indexes = await api.indexes();
        } catch (error) {
            // The panel keeps what it has: an empty panel would look like "the index lost its data".
            notice.textContent = `The indexes could not be read: ${error.message}`;
            notice.hidden = false;
            return;
        }

        notice.hidden = true;
        selected = selected && indexes.some(index => index.name === selected) ? selected : indexes[0]?.name ?? null;
        drawTabs(indexes);
        drawCard(indexes);
    }

    /** The tabs: one per index, with what it holds. */
    function drawTabs(indexes) {
        clear(tabs);
        for (const info of indexes) {
            tabs.append(el('button', {
                type: 'button',
                role: 'tab',
                class: 'tab',
                'aria-selected': String(info.name === selected),
                on: { click: () => { selected = info.name; drawTabs(indexes); drawCard(indexes); } },
            },
                el('strong', { text: info.name }),
                el('span', { class: 'tab-count', text: number(info.count) })));
        }
    }

    /** The card of the index that is open — only that one. */
    function drawCard(indexes) {
        clear(host);
        const info = indexes.find(index => index.name === selected);
        if (info) {
            host.append(card(info));
        }
    }

    function card(info) {
        const output = el('pre', { class: 'output', 'aria-live': 'polite', text: outputs.get(info.name) ?? 'The result of an operation appears here.' });
        const run = async (label, action) => {
            output.textContent = `${label} …`;
            try {
                const result = await action();
                output.textContent = `${label}\n${JSON.stringify(result, null, 2)}`;
            } catch (error) {
                output.textContent = `${label}\n${error.status ?? ''} ${error.message}`;
            }
            outputs.set(info.name, output.textContent);
            bus.emit('index:changed', { index: info.name, note: label });
        };
        const button = (label, title, action) => el('button', { type: 'button', class: 'button', title, text: label, on: { click: () => run(label, action) } });
        const facts = [
            ['Kind', info.kind], ['Storage', info.storage], ['Entries', number(info.count)], ['Payload', info.payload],
            ['Typo distance', info.typoDistance], ['Typo dictionary', info.typoDictionaryState],
            ...(info.storage === 'Linked' ? [['Outbox (Pending)', number(info.pending)], ['Triggers', info.triggers]] : []),
        ];

        return el('div', { class: 'index-card' },
            info.error
                ? el('p', { class: 'error', text: `This index could not be read: ${info.error}` })
                : el('p', { class: 'muted', text: info.description }),
            info.error
                ? null
                : el('dl', { class: 'facts' }, ...facts.map(([name, value]) => el('div', { class: 'fact' }, el('dt', { text: name }), el('dd', { text: String(value) })))),
            info.source
                ? el('p', { class: 'muted', text: 'The button that fills this index stands in its tab above, under the input.' })
                : null,
            el('div', { class: 'buttons' },
                button('Sync', info.storage === 'Linked' ? 'Sync() — rebuild the index from the application’s table' : 'Sync(entries) — compare with the seed: add, change, remove', () => api.sync(info.name)),
                button('Check', 'Check() — the index against itself', () => api.check(info.name)),
                ...(info.storage === 'Embedded' ? [
                    button('Check vs seed', 'Check(entries) — the index against the seed, changes nothing', () => api.check(info.name, 'seed')),
                    button('Rebuild', 'Rebuild() — the derived structures anew', () => api.rebuild(info.name)),
                ] : []),
                button('Optimize', 'Optimize() — merge the segments', () => api.optimize(info.name)),
                button('Warm up typos', 'WarmUpTypoDictionary()', () => api.warmUpTypos(info.name)),
                button('Rebuild typos', 'RebuildTypoDictionary() — forgets removed words', () => api.rebuildTypos(info.name))),
            // The entries of a bulk index are the ones of its source: there is nothing to write into it by hand.
            info.storage === 'Embedded' ? (info.source ? null : embeddedForms(info, run)) : linkedForms(info, run, button),
            output);
    }

    function embeddedForms(info, run) {
        const text = el('input', { type: 'text', placeholder: 'text', 'aria-label': 'text', required: true });
        const weight = el('input', { type: 'number', min: 0, max: 536870911, value: 100, 'aria-label': 'weight' });
        const priority = el('select', { 'aria-label': 'priority' }, ...meta.priority.map(name => el('option', { value: name, text: name, selected: name === 'Normal' })));
        const payload = info.payload === 'none' ? null : el('input', { type: 'text', value: '{"category":"Kuchen","minutes":30,"emoji":"🍰"}', 'aria-label': 'payload JSON' });
        const key = el('input', { type: 'number', placeholder: 'key', 'aria-label': 'key' });
        const newWeight = el('input', { type: 'number', min: 0, max: 536870911, placeholder: 'weight', 'aria-label': 'new weight' });
        const newPriority = el('select', { 'aria-label': 'priority' }, el('option', { value: '', text: '(priority)' }), ...meta.priority.map(name => el('option', { value: name, text: name })));
        const parse = () => (payload?.value ? JSON.parse(payload.value) : undefined);

        return el('div', { class: 'forms' },
            el('form', { class: 'form', on: { submit: event => { event.preventDefault(); run('Upsert', () => api.upsert(info.name, { text: text.value, weight: Number(weight.value), priority: priority.value, payload: parse() })); } } },
                el('strong', { text: 'Upsert' }), text, weight, priority, payload, el('button', { class: 'button', type: 'submit', text: 'Add or replace' })),
            el('form', { class: 'form', on: { submit: event => { event.preventDefault(); } } },
                el('strong', { text: 'By key' }), key, newWeight, newPriority,
                el('button', { class: 'button', type: 'button', text: 'UpdateWeight', on: { click: () => run('UpdateWeight', () => api.patch(info.name, key.value, { weight: Number(newWeight.value) })) } }),
                el('button', { class: 'button', type: 'button', text: 'UpdatePriority', on: { click: () => run('UpdatePriority', () => api.patch(info.name, key.value, { priority: newPriority.value })) } }),
                el('button', { class: 'button danger', type: 'button', text: 'Remove', on: { click: () => run('Remove', () => api.remove(info.name, key.value)) } })));
    }

    function linkedForms(info, run, button) {
        const name = el('input', { type: 'text', placeholder: 'name', 'aria-label': 'name', required: true });
        const kind = el('input', { type: 'text', value: 'Anlass', 'aria-label': 'kind' });
        const color = el('input', { type: 'color', value: '#3182ce', 'aria-label': 'colour' });
        const usage = el('input', { type: 'number', min: 0, value: 10, 'aria-label': 'usage_count' });
        const rank = el('select', { 'aria-label': 'rank' }, ...meta.priority.map((label, i) => el('option', { value: i, text: `${i} ${label}`, selected: i === 3 })));
        const id = el('input', { type: 'number', placeholder: 'id', 'aria-label': 'id' });

        return el('div', { class: 'forms' },
            el('form', { class: 'form', on: { submit: event => { event.preventDefault(); run('INSERT INTO tag', () => api.upsertRow(info.name, { name: name.value, kind: kind.value, color: color.value, usageCount: Number(usage.value), rank: Number(rank.value) })); } } },
                el('strong', { text: 'Write to the application’s table' }), name, kind, color, usage, rank,
                el('button', { class: 'button', type: 'submit', text: 'Add a row' })),
            el('div', { class: 'form' },
                el('strong', { text: 'Tell the index' }), id,
                el('button', { class: 'button', type: 'button', text: 'Delete row', on: { click: () => run('DELETE FROM tag', () => api.deleteRow(info.name, id.value)) } }),
                button('Refresh id', 'RefreshMany([id]) — read the row again', () => api.refresh(info.name, [Number(id.value)])),
                button('Apply pending', 'ApplyPending() — apply the outbox now (the host does it every two seconds)', () => api.applyPending(info.name)),
                button('Create triggers', 'CreateTriggers() — the application’s decision (ADR-0015)', () => api.triggers(info.name, true)),
                button('Drop triggers', 'DropTriggers() — then only Refresh/Sync tell the index', () => api.triggers(info.name, false))));
    }

    bus.on('index:changed', () => { refresh().catch(() => {}); });
    refresh().catch(error => { notice.textContent = `The indexes could not be read: ${error.message}`; notice.hidden = false; });
}
