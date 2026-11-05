import { api } from '../net/api.js';
import { clear, el } from '../core/dom.js';
import { number } from '../core/format.js';
import { mountFillForm } from './fill-form.js';

/**
 * The rest of the public surface of LUI, as buttons and forms: what an index is, what the application writes to it, and
 * how it is kept in order. The suggest route is the other panels; this is §12.4 – §12.7 of the specification.
 *
 *  - **embedded** (`recipes`, `names`): the entries are LUI's — `Upsert`, `UpdateWeight`, `UpdatePriority`, `Remove`, and
 *    `Sync` against the seed;
 *  - **linked** (`tags`): the text is the application's table — the page writes the row, and tells the index (the triggers
 *    and the outbox, `Refresh`, `ApplyPending`).
 *
 * @param {{ root: HTMLElement, bus: ReturnType<typeof import('../core/bus.js').createBus>, meta: any }} options
 */
export function mountIndexesPanel({ root, bus, meta }) {
    const cards = el('div', { class: 'index-cards' });
    /** What the page remembers across a redraw of the cards: the last output and whether a card is open. */
    const outputs = new Map();
    const opened = new Map();
    root.append(el('h2', { text: 'The indexes' }), cards);

    async function refresh() {
        const indexes = await api.indexes();
        clear(cards);
        for (const info of indexes) {
            cards.append(card(info));
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
            ['TypoTolerance', info.typoTolerance], ['Typo dictionary', info.typoDictionaryState],
            ...(info.storage === 'Linked' ? [['Outbox (Pending)', number(info.pending)], ['Triggers', info.triggers]] : []),
        ];

        return el('details', {
            class: 'index-card',
            open: opened.get(info.name) ?? (info.name === 'recipes' || info.name === 'tags'),
            on: { toggle: event => opened.set(info.name, event.target.open) },
        },
            el('summary', {}, el('strong', { text: info.name }), el('span', { class: 'badge', text: `${info.storage} · ${info.kind}` })),
            el('p', { class: 'muted', text: info.description }),
            el('dl', { class: 'facts' }, ...facts.map(([name, value]) => el('div', { class: 'fact' }, el('dt', { text: name }), el('dd', { text: String(value) })))),
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
            info.storage === 'Linked' ? linkedForms(info, run, button) : embeddedForms(info, run),
            info.source ? mountFillForm({ info, bus }) : null,
            output);
    }

    function embeddedForms(info, run) {
        const text = el('input', { type: 'text', placeholder: 'text', 'aria-label': 'text', required: true });
        const weight = el('input', { type: 'number', min: 0, max: 536870911, value: 100, 'aria-label': 'weight' });
        const priority = el('select', { 'aria-label': 'priority' }, ...meta.priority.map(name => el('option', { value: name, text: name, selected: name === 'Normal' })));
        const payload = info.payload === 'none' ? null : el('input', { type: 'text', value: '{"category":"Kuchen","minutes":30,"emoji":"🍰"}', 'aria-label': 'payload JSON' });
        const key = el('input', { type: 'number', placeholder: 'key', 'aria-label': 'key' });
        const newWeight = el('input', { type: 'number', min: 0, max: 536870911, placeholder: 'weight', 'aria-label': 'new weight' });
        const newPriority = el('select', { 'aria-label': 'new priority' }, el('option', { value: '', text: '(priority)' }), ...meta.priority.map(name => el('option', { value: name, text: name })));
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
    refresh().catch(error => root.append(el('p', { class: 'error', text: error.message })));
}
