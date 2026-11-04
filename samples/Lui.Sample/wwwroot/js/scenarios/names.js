import { baseItem } from '../suggest/item.js';

/**
 * Names: an **embedded, multi-word** index **without a payload**, with typo tolerance. Nobody knows how "Schröder" is
 * spelled. The dictionary of this index is its own: a correction here never goes towards a tag, and a search among tags
 * never towards a name (ADR-0025) — try "megn" here and in the tag picker.
 */
export const names = {
    id: 'names',
    index: 'names',
    title: 'Names',
    kind: 'Embedded · MultiWord · no payload',
    summary:
        'The simplest index: a text and a weight. The typo tolerance corrects what has no word start (not only what finds nothing), ' +
        'and the dictionary belongs to this index alone — "megn" becomes "megan" here and finds nothing among the tags.',
    hints: [
        { text: 'megn', note: 'typo → mega…' },
        { text: 'schroeder', note: 'ö as oe' },
        { text: 'mueller', note: 'ü as ue — finds Müller too' },
        { text: 'müler', note: 'typo + umlaut' },
        { text: 'lukasz', note: 'Ł folds to l' },
    ],

    /** @param {any} envelope @returns {any[]} */
    presenter: envelope => envelope.results.map(hit => baseItem(hit, envelope)),

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
