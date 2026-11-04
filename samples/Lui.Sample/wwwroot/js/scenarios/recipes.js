import { baseItem, display } from '../suggest/item.js';

/**
 * Recipes: an **embedded, multi-word** index whose entries carry a **JSON payload** — category, minutes, emoji — that LUI
 * stores next to the text. The payload is what makes the list more than a list: flexdatalist groups by the category,
 * shows the time as meta, and the emoji leads each line.
 */
export const recipes = {
    id: 'recipes',
    index: 'recipes',
    title: 'Recipes',
    kind: 'Embedded · MultiWord · JSON payload',
    summary:
        'The payload of a hit — {category, minutes, emoji} — comes out of LUI with the text. flexdatalist groups by the category ' +
        'and shows the rest. A second word finds a hit too, a priority puts a badge on it, and a typo is corrected.',
    hints: [
        { text: 'apfelkuch', note: 'prefix; the best match first' },
        { text: 'kuchen', note: 'second word of a compound text' },
        { text: 'kartofel', note: 'typo → corrected query' },
        { text: 'kaesekuchen', note: 'transliteration: ae = ä' },
        { text: 'spätzle', note: 'umlaut as typed' },
    ],

    /** @param {any} envelope @returns {any[]} */
    presenter: envelope => envelope.results.map(hit => {
        const item = baseItem(hit, envelope);
        display(item, 'emoji', hit.payload?.emoji ?? '');
        display(item, 'category', hit.payload?.category ?? 'Other');
        display(item, 'meta', hit.payload ? `${hit.payload.minutes} min` : '');
        return item;
    }),

    /** @param {{ grouped: boolean }} ui */
    flex: ui => ({
        visibleProperties: ['emoji', 'label', 'badge', 'meta'],
        groupBy: ui.grouped ? 'category' : false,
    }),

    /** @param {any} item @returns {Array<[string, string]>} */
    describe: item => [
        ['Recipe', `${item.emoji} ${item.label}`],
        ['Category (payload)', item.category],
        ['Time (payload)', item.meta],
        ['Key', String(item.key)],
        ['Weight · priority', `${item.weight} · ${item.priority}`],
    ],
};
