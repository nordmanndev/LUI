import { api } from '../net/api.js';
import { el } from '../core/dom.js';
import { clock } from '../core/format.js';

const MAX_ENTRIES = 80;

/**
 * Every request the page makes for a suggestion, and how it ended: answered, **aborted** (the page cancelled it, the
 * server stopped), or refused. Beside the page's own count, the server's: an abort that only the browser noticed would
 * not have saved the server anything.
 *
 * @param {{ root: HTMLElement, bus: ReturnType<typeof import('../core/bus.js').createBus> }} options
 */
export function mountActivityPanel({ root, bus }) {
    const counts = { sent: 0, answered: 0, aborted: 0, refused: 0 };
    const summary = el('p', { class: 'counters' });
    const server = el('p', { class: 'counters muted' });
    const log = el('ol', { class: 'log', 'aria-live': 'off' });
    root.append(el('h2', { text: 'Requests' }), summary, server, log);

    const paint = () => {
        summary.textContent = `page: ${counts.sent} sent · ${counts.answered} answered · ${counts.aborted} aborted · ${counts.refused} refused`;
    };
    paint();

    let pending = null;
    const refreshServer = () => {
        clearTimeout(pending);
        pending = setTimeout(async () => {
            try {
                const { suggest } = await api.health();
                server.textContent = `server: ${suggest.served} answered · ${suggest.aborted} stopped by an abort`;
            } catch {
                server.textContent = 'server: no answer';
            }
        }, 400);
    };

    const add = (kind, index, text) => {
        log.prepend(el('li', { class: `entry ${kind}` }, el('time', { text: clock() }), el('b', { text: kind }), el('span', { class: 'index', text: index }), el('code', { text })));
        while (log.children.length > MAX_ENTRIES) {
            log.lastElementChild.remove();
        }
    };

    bus.on('suggest:request', ({ index, url }) => { counts.sent++; paint(); add('request', index, url); });
    bus.on('suggest:response', ({ index, envelope, roundtripMs }) => {
        counts.answered++; paint();
        add('answered', index, `${envelope.results.length} hits in ${roundtripMs.toFixed(0)} ms`);
        refreshServer();
    });
    bus.on('suggest:aborted', ({ index }) => { counts.aborted++; paint(); add('aborted', index, 'cancelled by the page'); refreshServer(); });
    bus.on('suggest:error', ({ index, status, detail }) => { counts.refused++; paint(); add('refused', index, `${status || 'network'}: ${detail}`); });
    bus.on('index:changed', ({ index, note }) => add('write', index, note ?? 'changed'));
}
