import { toParams } from './settings.js';

/**
 * The options of one flexdatalist input, for one scenario: what is the same for every scenario (it is a LUI client), then
 * what the scenario adds (`scenario.flex`). Each line that is not obvious says what it is for — they are the answers of the
 * check of flexdatalist 3.1.1 (vendor/README.md).
 *
 * @param {{ index: string, flex(ui: any): object }} scenario
 * @param {{ get(): any, defaults: any }} settings
 * @returns {object} the options for `Flexdatalist.init`
 */
export function buildFlexOptions(scenario, settings) {
    const { ui } = settings.get();

    return {
        url: `/api/suggest/${scenario.index}`,
        // a function: evaluated for every request, so a knob that moved applies to the next keystroke
        params: () => toParams(settings.get(), settings.defaults),
        keywordParamName: 'q',
        resultsProperty: 'results',

        // The server ranks, corrects typos and transliterates. flexdatalist's own filter would drop what it cannot match
        // literally — `gedekter` → `Gedeckter Apfelkuchen` — and with it switched off, nothing is highlighted: see `item.js`.
        searchDisabled: true,
        // Keyed by keyword, not by the parameters: with a changed setting it would show the hits of the old one.
        cache: false,
        // A request that is aborted is not an error (see `net/suggest-transport.js`).
        debug: false,

        minLength: ui.minLength,
        searchDelay: ui.searchDelay,
        maxShownResults: ui.maxShown,
        focusFirstResult: true,
        searchIn: ['label'],
        textProperty: 'label',
        noResultsText: 'Nothing found for "{keyword}"',

        ...scenario.flex(ui),
    };
}
