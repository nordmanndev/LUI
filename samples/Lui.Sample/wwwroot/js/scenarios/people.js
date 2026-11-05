import { baseItem } from '../suggest/item.js';

/**
 * People: an **embedded, multi-word** index **without a payload**, filled with the full names of Bogus's German locale. The
 * dictionary of this index is its own (ADR-0025), so a correction here never goes towards a tag — and a search among tags never
 * towards a name. The entries are generated, not checked in: the button in the index panel below loads as many as you ask for.
 */
export const people = {
    id: 'people',
    index: 'people',
    title: 'People',
    kind: 'Embedded · MultiWord · no payload · bulk',
    summary:
        'The full names of Bogus’s German locale — as many as the button in the index panel below loads. Several words, so the ' +
        'order does not matter ("gehrke mer yem" finds "Meryem Gehrke"), and the typo tolerance corrects what has no word start.',
    hints: [
        { text: 'Gehrke', note: 'the second word of a name' },
        { text: 'Schmidt', note: 'a name of its own' },
        { text: 'Wöschler', note: 'the umlaut folds' },
        { text: 'Koob', note: 'a short name' },
    ],

    /** @param {any} envelope @returns {any[]} */
    presenter: envelope => envelope.results.map(hit => baseItem(hit)),

    flex: () => ({
        visibleProperties: ['label', 'badge'],
        groupBy: false,
    }),

    /** @param {any} item @returns {Array<[string, string]>} */
    describe: item => [
        ['Name', item.label],
        ['Key', String(item.key)],
        ['Weight · priority', `${item.weight} · ${item.priority}`],
        ['Payload', 'none'],
    ],
};
