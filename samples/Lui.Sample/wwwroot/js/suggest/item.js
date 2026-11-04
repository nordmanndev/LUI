import { escapeHtml } from '../core/dom.js';

/**
 * Makes the item flexdatalist renders out of a hit.
 *
 * flexdatalist writes the value of every `visibleProperties` entry — and the group header — with `innerHTML`, and prefers
 * `<property>_highlight` when the item has one. Without it the **raw value** is written, so a hit whose text is
 * `<img src=x onerror=…>` would run. Therefore: **every property an item displays has an escaped `_highlight` twin**, built
 * here and nowhere else. (The name is the library's; the twin carries no highlighting — the page marks nothing, ADR-0028.)
 * `display()` is the way to add one; the tests put hostile text through every presenter.
 */

/**
 * Adds the property and its escaped twin.
 *
 * @param {Record<string, any>} item
 * @param {string} name
 * @param {string|number} value
 * @param {string} [markup] markup the page itself built (the colour dot), when the twin is not just the escaped value
 */
export function display(item, name, value, markup) {
    item[name] = String(value);
    item[`${name}_highlight`] = markup ?? escapeHtml(String(value));
}

/**
 * The properties every hit has: its text, and a badge for a priority above normal — so the effect of a priority is visible in
 * the list, not only in the order.
 *
 * @param {any} hit a hit of the sample's API
 * @returns {Record<string, any>}
 */
export function baseItem(hit) {
    const item = {
        key: hit.key,
        weight: hit.weight,
        priority: hit.priority,
        similarity: hit.similarity,
        atWordStart: hit.atWordStart,
    };
    display(item, 'label', hit.text);
    if (hit.priority !== 'Normal') {
        display(item, 'badge', hit.priority);
    }
    return item;
}
