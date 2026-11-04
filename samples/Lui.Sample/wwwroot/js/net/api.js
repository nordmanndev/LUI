import { deleteJson, getJson, patchJson, postJson } from './http.js';

/**
 * The routes of the sample's API, one function each, named like the thing of LUI they call. Nothing here knows the page.
 * The suggest route is not here: it is called by flexdatalist, through `suggest-transport.js`.
 */
const index = name => `/api/indexes/${encodeURIComponent(name)}`;

export const api = {
    meta: () => getJson('/api/meta'),
    health: () => getJson('/api/health'),
    indexes: () => getJson('/api/indexes'),

    // embedded: the entries
    upsert: (name, entry) => postJson(`${index(name)}/entries`, entry),
    patch: (name, key, change) => patchJson(`${index(name)}/entries/${key}`, change),
    remove: (name, key) => deleteJson(`${index(name)}/entries/${key}`),
    rebuild: name => postJson(`${index(name)}/rebuild`),

    // linked: the application's table, and telling the index
    upsertRow: (name, row) => postJson(`${index(name)}/rows`, row),
    deleteRow: (name, id) => deleteJson(`${index(name)}/rows/${id}`),
    refresh: (name, keys) => postJson(`${index(name)}/refresh`, { keys }),
    applyPending: name => postJson(`${index(name)}/apply-pending`),
    triggers: (name, create) => postJson(`${index(name)}/triggers`, { create }),

    // both: maintenance
    sync: name => postJson(`${index(name)}/sync`),
    check: (name, source) => getJson(`${index(name)}/check${source ? `?source=${source}` : ''}`),
    optimize: name => postJson(`${index(name)}/optimize`),
    warmUpTypos: name => postJson(`${index(name)}/typos/warm-up`),
    rebuildTypos: name => postJson(`${index(name)}/typos/rebuild`),
};
