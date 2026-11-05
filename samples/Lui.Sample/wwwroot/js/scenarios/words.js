import { baseItem } from '../suggest/item.js';

/**
 * Words: an **embedded, single-word** index **without a payload**, filled with the German word list — 685,829 words when the
 * list has been downloaded, 7,071 when the sample falls back to the curated subset. This is the stock of short texts LUI is
 * built for: fill the index in the panel below, and the query stays in the milliseconds while it holds half a million words.
 */
export const words = {
    id: 'words',
    index: 'words',
    title: 'Words',
    kind: 'Embedded · SingleWord · no payload · bulk',
    summary:
        'The German word list, one word per entry. The index is empty until it is filled — the button below ' +
        'loads it with the number of entries you ask for, and the query stays in the milliseconds while it grows.',
    hints: [
        { text: 'aalf', note: 'prefix' },
        { text: 'abachet', note: 'a word of the list' },
        { text: 'abbruch', note: 'the case does not matter' },
        { text: 'abeced', note: 'the prefix of a long word' },
    ],

    /** @param {any} envelope @returns {any[]} */
    presenter: envelope => envelope.results.map(hit => baseItem(hit)),

    flex: () => ({
        visibleProperties: ['label', 'badge'],
        groupBy: false,
    }),

    /** @param {any} item @returns {Array<[string, string]>} */
    describe: item => [
        ['Word', item.label],
        ['Key', String(item.key)],
        ['Weight · priority', `${item.weight} · ${item.priority}`],
        ['Payload', 'none'],
    ],
};
