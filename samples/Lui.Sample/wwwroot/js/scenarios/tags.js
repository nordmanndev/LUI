import { baseItem, display } from '../suggest/item.js';

const HEX_COLOR = /^#[0-9a-fA-F]{6}$/;

/**
 * Tags: an index **linked to a table of the application** (`tag`). The text, the weight (`usage_count`), the priority (`rank`)
 * and the payload (`kind`, `color`) are columns; LUI reads them and stores nothing. `vegan` has the priority System: it comes
 * first for every input it matches. In flexdatalist this is a **tag picker**: several values, a result is required, and
 * what is not found can be added — the page writes a row to the application's table and tells the index.
 */
export const tags = {
    id: 'tags',
    index: 'tags',
    title: 'Tags',
    kind: 'Linked · SingleWord · column payload · priority',
    summary:
        'Pick tags as chips. They live in the application’s own table; the colour and the kind are columns, read as the payload. ' +
        '"vegan" has the priority System and leads. A tag that does not exist can be added: the page writes the row, the outbox ' +
        'and the hosted service (or "Apply pending") bring it into the index.',
    hints: [
        { text: 've', note: 'priority System first' },
        { text: 'vegn', note: 'typo — tags have their own dictionary' },
        { text: 'megn', note: 'a name, not a tag: nothing' },
        { text: 'grill', note: 'prefix' },
        { text: 'neues tag', note: 'not found → add it' },
    ],

    /** @param {any} envelope @returns {any[]} */
    presenter: envelope => envelope.results.map(hit => {
        const item = baseItem(hit);
        const color = HEX_COLOR.test(hit.payload?.color ?? '') ? hit.payload.color : '#718096';
        display(item, 'kind', hit.payload?.kind ?? 'Sonstiges');
        // the colour is set on the element by `swatches.js` after the list is drawn: an inline style would be refused by the policy
        display(item, 'swatch', color, `<span class="swatch" data-color="${color}"></span>`);
        return item;
    }),

    flex: ui => ({
        multiple: true,
        selectionRequired: true,
        showAddNewItem: true,
        addNewItemText: 'No tag "{keyword}". Add it to the table.',
        visibleProperties: ['swatch', 'label', 'badge'],
        groupBy: ui.grouped ? 'kind' : false,
        valueProperty: 'key',
        textProperty: 'label',
    }),

    /** @param {any} item @returns {Array<[string, string]>} */
    describe: item => [
        ['Tag', item.label],
        ['Kind (payload column)', item.kind],
        ['Colour (payload column)', item.swatch],
        ['Key (the table’s id)', String(item.key)],
        ['Usage · priority', `${item.weight} · ${item.priority}`],
    ],

    /**
     * flexdatalist offers to add what it did not find (`showAddNewItem`). A linked index is the application’s table: the page
     * writes the row, tells the index (the outbox, applied now instead of in two seconds) and searches again.
     */
    async onAddNew(keyword, { api, bus, search }) {
        const { id } = await api.upsertRow('tags', { name: keyword.trim(), kind: 'Neu', usageCount: 1 });
        await api.applyPending('tags');
        bus.emit('index:changed', { index: 'tags', note: `row ${id} written to the table, outbox applied` });
        search(keyword);
    },
};
