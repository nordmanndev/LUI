import { clear, el } from '../core/dom.js';
import { priorityClass, ratio } from '../core/format.js';

/**
 * The ranking, made visible: for each hit what LUI ranked by — priority, whether a word begins with the input, similarity,
 * weight — and the payload it carried. A rule that is only asserted in a test is easy to doubt; here it is a table.
 *
 * @param {{ root: HTMLElement, bus: ReturnType<typeof import('../core/bus.js').createBus> }} options
 */
export function mountHitsPanel({ root, bus }) {
    const body = el('div', { class: 'hits' });
    root.append(el('h2', { text: 'The hits, as ranked' }), body);
    body.append(el('p', { class: 'muted', text: 'Nothing yet.' }));

    bus.on('suggest:response', ({ index, envelope }) => {
        clear(body);
        if (envelope.results.length === 0) {
            body.append(el('p', { class: 'muted', text: `No hits in "${index}" for ${JSON.stringify(envelope.correctedQuery ?? envelope.query)}.` }));
            return;
        }

        body.append(el('table', { class: 'table' },
            el('thead', {}, el('tr', {}, ...['#', 'Text', 'Key', 'Priority', 'Word start', 'Similarity', 'Weight', 'Payload'].map(name => el('th', { text: name })))),
            el('tbody', {}, ...envelope.results.map((hit, i) => el('tr', {},
                el('td', { text: i + 1 }),
                el('td', { class: 'text', text: hit.text }),
                el('td', { text: hit.key }),
                el('td', {}, el('span', { class: priorityClass(hit.priority), text: hit.priority })),
                el('td', { text: hit.atWordStart ? '✓' : '·' }),
                el('td', { text: ratio(hit.similarity) }),
                el('td', { text: hit.weight }),
                el('td', { class: 'payload', text: hit.payload ? JSON.stringify(hit.payload) : '–' }))))));
    });
}
