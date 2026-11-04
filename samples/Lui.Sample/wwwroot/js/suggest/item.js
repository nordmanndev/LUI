import { escapeHtml } from '../core/dom.js';
import { highlight } from './highlight.js';

/**
 * Makes the item flexdatalist renders out of a hit.
 *
 * flexdatalist writes the value of every `visibleProperties` entry — and the group header — with `innerHTML`, and prefers
 * `<property>_highlight` when the item has one. Without it the **raw** value is written, so a hit whose text is
 * `<img src=x onerror=…>` would run. Therefore: **every property an item displays has an escaped `_highlight` twin**, built
 * here and nowhere else. `display()` is the way to add one; the tests put hostile text through every presenter.
 */

/**
 * Adds the property and its escaped twin.
 *
 * @param {Record<string, any>} item
 * @param {string} name
 * @param {string|number} value
 * @param {string} [markedUp] already escaped HTML, when the property is highlighted
 */
export function display(item, name, value, markedUp) {
    item[name] = String(value);
    item[`${name}_highlight`] = markedUp ?? escapeHtml(String(value));
}

/**
 * The properties every hit has: its text (highlighted for the query that found it), and a badge for a priority above
 * normal — so the effect of a priority is visible in the list, not only in the order.
 *
 * @param {any} hit a hit of the sample's API
 * @param {any} envelope the response it came in
 * @returns {Record<string, any>}
 */
export function baseItem(hit, envelope) {
    const searched = envelope.correctedQuery ?? envelope.query;
    const item = {
        key: hit.key,
        weight: hit.weight,
        priority: hit.priority,
        similarity: hit.similarity,
        atWordStart: hit.atWordStart,
    };
    display(item, 'label', hit.text, highlight(hit.text, searched));
    if (hit.priority !== 'Normal') {
        display(item, 'badge', hit.priority);
    }
    return item;
}
