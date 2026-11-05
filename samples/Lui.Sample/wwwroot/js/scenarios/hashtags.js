import { baseItem } from '../suggest/item.js';

/**
 * Hashtags: an **embedded, single-word** index **without a payload**, filled with the tags of Bogus's German locale — a place
 * and a surname, joined by a hyphen. A tag of two words is found by either of them: the index of a single word searches the text
 * as a whole, and its trigrams with it.
 */
export const hashtags = {
    id: 'hashtags',
    index: 'hashtags',
    title: 'Hashtags',
    kind: 'Embedded · SingleWord · no payload · bulk',
    summary:
        'The tags of Bogus’s German locale: "#Cedrikdorf-Gehrke". The index is empty until the button in the index panel below ' +
        'fills it — and a tag of two words is found by either of them.',
    hints: [
        { text: 'Cedrikdorf', note: 'the place of a tag' },
        { text: 'Koob', note: 'the surname of a tag' },
        { text: 'Strellerburg', note: 'another place' },
        { text: 'Kahles', note: 'another surname' },
    ],

    /** @param {any} envelope @returns {any[]} */
    presenter: envelope => envelope.results.map(hit => baseItem(hit)),

    flex: () => ({
        visibleProperties: ['label', 'badge'],
        groupBy: false,
    }),

    /** @param {any} item @returns {Array<[string, string]>} */
    describe: item => [
        ['Tag', item.label],
        ['Key', String(item.key)],
        ['Weight · priority', `${item.weight} · ${item.priority}`],
        ['Payload', 'none'],
    ],
};
