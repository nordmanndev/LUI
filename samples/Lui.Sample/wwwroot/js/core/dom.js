/**
 * The only place that builds DOM for the panels — and it never parses HTML: text goes in as a text node, so a hostile
 * entry cannot become markup. (The guard `Sample_Client` fails the build on innerHTML, outerHTML, insertAdjacentHTML and
 * document.write anywhere in the page's own modules.)
 */

/**
 * @param {string} tag
 * @param {Record<string, any>} [props]  `class`, `text`, `dataset`, `on` (event handlers), anything else is an attribute
 * @param {...(Node|string|null|undefined|false)} children
 * @returns {HTMLElement}
 */
export function el(tag, props = {}, ...children) {
    const node = document.createElement(tag);
    for (const [name, value] of Object.entries(props)) {
        if (value === undefined || value === null || value === false) {
            continue;
        }
        if (name === 'class') {
            node.className = value;
        } else if (name === 'text') {
            node.textContent = value;
        } else if (name === 'dataset') {
            Object.assign(node.dataset, value);
        } else if (name === 'on') {
            for (const [type, handler] of Object.entries(value)) {
                node.addEventListener(type, handler);
            }
        } else if (value === true) {
            node.setAttribute(name, '');
        } else {
            node.setAttribute(name, String(value));
        }
    }
    node.append(...children.filter(child => child !== null && child !== undefined && child !== false));
    return node;
}

/** Removes the children of a node. */
export function clear(node) {
    node.replaceChildren();
    return node;
}

/**
 * Escapes text for the places where a string of HTML is unavoidable — the one such place is the string flexdatalist
 * receives as `<property>_highlight` (the library writes it with innerHTML).
 *
 * @param {string} text
 * @returns {string}
 */
export function escapeHtml(text) {
    return String(text)
        .replaceAll('&', '&amp;')
        .replaceAll('<', '&lt;')
        .replaceAll('>', '&gt;')
        .replaceAll('"', '&quot;')
        .replaceAll("'", '&#39;');
}
