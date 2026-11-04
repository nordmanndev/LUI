import { el } from '../core/dom.js';

/**
 * "Did you mean …?" — what the page says when LUI corrected the input.
 *
 * The typo tolerance (spec §10) searches a corrected word besides the typed one, so the list can show hits the typed text does
 * not contain, while the input still holds the typo. The page does not hide that and does not try to mark it: it says it, in
 * one line above the list, and the line is a way to search the corrected word as it is. (The envelope's `correctedQuery` is
 * what the search used; it is the beginning of a word when the dictionary holds beginnings — "mega" for "megn".)
 */

/**
 * @param {any} envelope the response of the last suggestion
 * @returns {string|null} the corrected query, when LUI corrected and it is not what was typed
 */
export function correctionOf(envelope) {
    const corrected = envelope?.correctedQuery;
    if (!corrected) {
        return null;
    }
    return corrected.trim().toLowerCase() === String(envelope.query ?? '').trim().toLowerCase() ? null : corrected;
}

/**
 * Puts the line above the list whenever flexdatalist has drawn it. flexdatalist draws a grouped list group by group and says
 * so each time, so the line is removed and put again: there is one.
 *
 * @param {HTMLElement} input the original input of a flexdatalist instance: its events say when the list is drawn
 * @param {{ latest: () => any, choose: (corrected: string) => void }} options
 */
export function showDidYouMean(input, { latest, choose }) {
    input.addEventListener('shown:flexdatalist.results', () => {
        const list = document.querySelector('ul.flexdatalist-results');
        if (!list) {
            return;
        }

        list.querySelector(':scope > li.did-you-mean')?.remove();
        const corrected = correctionOf(latest());
        if (!corrected) {
            return;
        }

        // class "group": flexdatalist neither selects nor moves over a header
        list.prepend(el('li', { class: 'group did-you-mean' },
            'Did you mean ',
            el('button', {
                type: 'button',
                class: 'suggestion',
                text: `“${corrected}”`,
                on: {
                    // the input keeps its focus, or the list closes before the click
                    mousedown: event => event.preventDefault(),
                    click: () => choose(corrected),
                },
            }),
            '?'));
    });
}
