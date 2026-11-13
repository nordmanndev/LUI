/**
 * The knobs of the page. Two groups, kept apart because they belong to different libraries:
 *
 *  - `query`  — the settings of a LUI query (`LuiQueryOptions`), sent as parameters of every suggestion request;
 *  - `ui`     — the settings of the flexdatalist input (when it searches, how much it shows) and the demo latency.
 *
 * The defaults of `query` are not written here: the server tells them (`/api/meta`, `new LuiQueryOptions()`), so a default
 * of the library cannot drift from the page.
 */

/** @typedef {{ take: number, candidates: number, rank: string, priorityMode: string, similarity: string, typo: 'default'|'on'|'off', timings: boolean }} QuerySettings */
/** @typedef {{ minLength: number, searchDelay: number, maxShown: number, grouped: boolean, delayMs: number }} UiSettings */
/** @typedef {{ query: QuerySettings, ui: UiSettings }} Settings */

export const UI_DEFAULTS = Object.freeze({ minLength: 1, searchDelay: 120, maxShown: 50, grouped: true, delayMs: 0 });

/**
 * @param {any} libraryDefaults the `defaults` of `/api/meta`
 * @returns {Settings}
 */
export function defaultSettings(libraryDefaults) {
    return {
        query: {
            take: libraryDefaults.maxResults,
            candidates: libraryDefaults.candidates,
            rank: libraryDefaults.rank,
            priorityMode: libraryDefaults.priorityMode,
            similarity: libraryDefaults.similarity ?? '',
            typo: 'default',
            timings: true, // not the library's default (off): the envelope panel is what this page is for
        },
        ui: { ...UI_DEFAULTS },
    };
}

/**
 * The query-string parameters of a suggestion request: **only what differs from the library's default**, so the URL in the
 * activity log reads as the settings that were made. `timings` and `delayMs` are the exceptions the page decides on.
 *
 * @param {Settings} settings
 * @param {Settings} defaults
 * @returns {Record<string, string>}
 */
export function toParams(settings, defaults) {
    const { query, ui } = settings;
    const base = defaults.query;
    /** @type {Record<string, string>} */
    const params = {};

    if (query.take !== base.take) params.take = String(query.take);
    if (query.candidates !== base.candidates) params.candidates = String(query.candidates);
    if (query.rank !== base.rank) params.rank = query.rank;
    if (query.priorityMode !== base.priorityMode) params.priorityMode = query.priorityMode;
    if (query.similarity !== base.similarity) params.similarity = query.similarity;
    if (query.typo !== 'default') params.typo = String(query.typo === 'on');
    if (query.timings) params.timings = 'true';
    if (ui.delayMs > 0) params.delayMs = String(ui.delayMs);

    return params;
}

/**
 * A small observable store over the settings, persisted in the browser when it can be (it may not: private windows, blocked
 * storage — the page works without).
 *
 * @param {Settings} defaults
 * @param {Pick<Storage, 'getItem'|'setItem'> | null} storage
 * @param {(detail: { settings: Settings }) => void} onChange
 */
export function createSettings(defaults, storage, onChange) {
    const KEY = 'lui-sample:settings:v1';

    function load() {
        try {
            const stored = JSON.parse(storage?.getItem(KEY) ?? 'null');
            return stored
                ? { query: { ...defaults.query, ...stored.query }, ui: { ...defaults.ui, ...stored.ui } }
                : structuredClone(defaults);
        } catch {
            return structuredClone(defaults);
        }
    }

    let current = load();

    function save() {
        try {
            storage?.setItem(KEY, JSON.stringify(current));
        } catch {
            // nothing to do: the settings live on in memory
        }
    }

    return {
        defaults,
        get: () => current,

        /** @param {{ query?: Partial<QuerySettings>, ui?: Partial<UiSettings> }} patch */
        update(patch) {
            current = { query: { ...current.query, ...patch.query }, ui: { ...current.ui, ...patch.ui } };
            save();
            onChange({ settings: current });
        },

        reset() {
            current = structuredClone(defaults);
            save();
            onChange({ settings: current });
        },
    };
}
