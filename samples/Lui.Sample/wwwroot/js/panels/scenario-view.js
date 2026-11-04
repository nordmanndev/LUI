import { el, clear } from '../core/dom.js';
import { Flexdatalist } from '../vendor/flexdatalist.es.js';
import { buildFlexOptions } from '../suggest/flex-options.js';
import { showDidYouMean } from '../suggest/did-you-mean.js';
import { paintSwatches } from '../suggest/swatches.js';

/**
 * One autocomplete: the explanation, hints to try, the flexdatalist input, and what was chosen.
 *
 * The input is rebuilt when a setting of the *input* changes (flexdatalist reads its options once); the settings of the
 * *query* need no rebuild — they are a function the library calls for every request (`flex-options.js`).
 *
 * @param {{
 *   root: HTMLElement,
 *   scenario: any,
 *   settings: ReturnType<typeof import('../suggest/settings.js').createSettings>,
 *   transport: { cancel(index: string): void },
 *   api: any,
 *   bus: ReturnType<typeof import('../core/bus.js').createBus>,
 * }} options
 */
export function mountScenario({ root, scenario, settings, transport, api, bus }) {
    const field = el('div', { class: 'field' });
    const selection = el('div', { class: 'selection', 'aria-live': 'polite' });
    /** @type {Flexdatalist | null} */
    let instance = null;
    /** @type {Map<string, any>} */
    const chosen = new Map();
    /** The response of the last suggestion of this index: what "Did you mean" reads. */
    let latest = null;
    const unsubscribe = bus.on('suggest:response', ({ index, envelope }) => {
        if (index === scenario.index) {
            latest = envelope;
        }
    });

    const search = text => {
        const alias = document.getElementById(`fd-${scenario.id}-flexdatalist`);
        alias?.focus();
        instance?.search(text);
    };

    root.append(
        el('header', { class: 'scenario-head' },
            el('h2', { text: scenario.title }),
            el('span', { class: 'badge', text: scenario.kind })),
        el('p', { class: 'summary', text: scenario.summary }),
        el('div', { class: 'hints' },
            el('span', { class: 'hints-label', text: 'Try' }),
            ...scenario.hints.map(hint => el('button', {
                type: 'button',
                class: 'hint',
                title: hint.note,
                on: { click: () => search(hint.text) },
            }, el('code', { text: hint.text }), el('small', { text: hint.note })))),
        field,
        selection);

    function renderSelection() {
        clear(selection);
        if (chosen.size === 0) {
            selection.append(el('p', { class: 'muted', text: 'Nothing chosen yet.' }));
            return;
        }
        for (const item of chosen.values()) {
            selection.append(el('dl', { class: 'selected-item' },
                ...scenario.describe(item).flatMap(([name, value]) => [el('dt', { text: name }), el('dd', { text: value })])));
        }
    }

    function onSelect(event) {
        const item = event.detail;
        if (!item || item.isAddNew) {
            return;
        }
        if (!scenario.flex(settings.get().ui).multiple) {
            chosen.clear();
        }
        chosen.set(String(item.key), item);
        renderSelection();
    }

    /** In multiple mode the chips can be removed: drop what is no longer a value. */
    function onChange() {
        if (!scenario.flex(settings.get().ui).multiple || !instance) {
            return;
        }
        const values = new Set([].concat(instance.getValue() ?? []).flatMap(value => String(value).split(',')).filter(Boolean));
        for (const key of [...chosen.keys()]) {
            if (!values.has(key)) {
                chosen.delete(key);
            }
        }
        renderSelection();
    }

    async function start() {
        stop();
        const input = el('input', {
            id: `fd-${scenario.id}`,
            type: 'text',
            name: scenario.id,
            class: 'flexdatalist',
            autocomplete: 'off',
            placeholder: `Search ${scenario.title.toLowerCase()}…`,
            'aria-label': `${scenario.title} autocomplete`,
        });
        field.append(input);

        input.addEventListener('select:flexdatalist', onSelect);
        input.addEventListener('change:flexdatalist', onChange);
        input.addEventListener('addnew:flexdatalist', event => {
            scenario.onAddNew?.(event.detail, { api, bus, search })
                .catch(error => bus.emit('suggest:error', { index: scenario.index, status: error.status ?? 0, detail: error.message }));
        });
        paintSwatches(input);
        showDidYouMean(input, { latest: () => latest, choose: corrected => search(corrected) });

        [instance] = await Flexdatalist.init(input, buildFlexOptions(scenario, settings));

        // The abort: flexdatalist debounces but cancels nothing. A keystroke ends the request in flight now, before its
        // answer can be drawn for an input the user has already left.
        document.getElementById(`${input.id}-flexdatalist`)?.addEventListener('input', () => transport.cancel(scenario.index));
    }

    function stop() {
        instance?.closeResults();
        instance?.destroy(true);
        instance = null;
        clear(field);
    }

    /** Ends the input and stops listening: a scenario that is left leaves nothing behind. */
    function dispose() {
        stop();
        unsubscribe();
    }

    renderSelection();
    return { start, stop, dispose, search };
}
