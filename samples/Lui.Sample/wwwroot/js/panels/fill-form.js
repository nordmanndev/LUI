import { api } from '../net/api.js';
import { el } from '../core/dom.js';
import { fillProgress, number } from '../core/format.js';

/**
 * The form that fills a bulk index: how many entries it should hold, and what the fill is doing (ADR-0029).
 *
 * The fill runs on the server in the background — a fill of a million entries takes minutes — so the form polls the state of
 * the fill and shows the progress the library reports after every batch.
 *
 * @param {{ info: any, bus: ReturnType<typeof import('../core/bus.js').createBus> }} options
 * @returns {HTMLElement}
 */
export function mountFillForm({ info, bus }) {
    // The ceiling of the list, or the largest fill the sample offers (ADR-0029) — the server is the truth about it.
    const limit = info.capacity ?? 3_000_000;
    const status = el('p', { class: 'muted fill-status', 'aria-live': 'polite', text: fillProgress(null) });
    const bar = el('progress', { class: 'progress', max: limit, value: 0 });
    const entries = el('input', { type: 'number', min: 1, max: limit, value: defaultCount(info), 'aria-label': 'entries' });
    const button = el('button', { class: 'button', type: 'submit', text: 'Create & fill' });
    const cancel = el('button', { class: 'button danger', type: 'button', text: 'Cancel', disabled: true, on: { click: stopFill } });
    let timer = null;

    const form = el('form', { class: 'form fill-form', 'data-index': info.name, on: { submit: event => { event.preventDefault(); start(); } } },
        el('strong', { text: 'Fill the index' }),
        el('span', { class: 'muted', text: info.source }),
        entries,
        el('span', { class: 'muted', text: `entries (at most ${number(limit)})` }),
        button,
        cancel,
        status,
        bar);

    async function start() {
        button.disabled = true;
        cancel.disabled = false;
        try {
            show(await api.fill(info.name, Number(entries.value)));
            poll();
        } catch (error) {
            status.textContent = `The fill did not start: ${error.status ?? ''} ${error.message}`.trim();
            button.disabled = false;
            cancel.disabled = true;
        }
    }

    async function stopFill() {
        try {
            await api.cancelFill(info.name);
        } catch (error) {
            status.textContent = `The fill could not be stopped: ${error.message}`;
        }
    }

    function poll() {
        stopPolling();
        timer = setTimeout(async () => {
            try {
                const state = await api.fillStatus(info.name);
                show(state);
                if (state?.state === 'running') {
                    poll();
                } else {
                    button.disabled = false;
                    cancel.disabled = true;
                    bus.emit('index:changed', { index: info.name, note: 'Fill' });
                }
            } catch (error) {
                status.textContent = `The progress is unknown: ${error.message}`;
                button.disabled = false;
                cancel.disabled = true;
            }
        }, 700);
    }

    function stopPolling() {
        if (timer !== null) {
            clearTimeout(timer);
            timer = null;
        }
    }

    function show(state) {
        status.textContent = fillProgress(state);
        bar.value = state?.read ?? 0;
    }

    // the fill of a previous visit, or one that runs while the page is open
    api.fillStatus(info.name)
        .then(state => {
            show(state);
            if (state?.state === 'running') {
                cancel.disabled = false;
                poll();
            }
        })
        .catch(() => {});

    return form;
}

/** The number of entries the form offers: ten thousand, or the whole list when it is shorter. */
function defaultCount(info) {
    const capacity = info.capacity ?? 0;
    return capacity > 0 ? Math.min(10_000, capacity) : 10_000;
}
