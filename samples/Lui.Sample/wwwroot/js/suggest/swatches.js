/**
 * Paints the colour dots of the tag list. The colour is a column of the application's table — data — and the page's policy
 * refuses inline styles, so the markup carries `data-color` (validated when it is built, `scenarios/tags.js`) and the
 * colour is set here, through the CSS object model, which the policy allows.
 *
 * @param {HTMLElement} input the original input of a flexdatalist instance: its events say when the list is drawn
 */
export function paintSwatches(input) {
    input.addEventListener('shown:flexdatalist.results', () => {
        for (const swatch of document.querySelectorAll('ul.flexdatalist-results .swatch[data-color]')) {
            swatch.style.backgroundColor = swatch.dataset.color;
        }
    });
}
