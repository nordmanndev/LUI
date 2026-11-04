import { clear, el } from '../core/dom.js';
import { ms, number } from '../core/format.js';

const LIBRARY_PARAMETERS = new Set(['selected', 'original', 'contain']);

const SEGMENTS = [
    ['candidateReadMs', 'candidates', 'seg-candidates'],
    ['correctionMs', 'correction', 'seg-correction'],
    ['rankingMs', 'ranking', 'seg-ranking'],
    ['payloadMs', 'payload', 'seg-payload'],
];

/**
 * What flexdatalist drops: the whole `LuiResult` around the hits — the query, the corrected query, whether the correction
 * was skipped or the list truncated, the candidates considered, the timings — and the request that made it.
 *
 * @param {{ root: HTMLElement, bus: ReturnType<typeof import('../core/bus.js').createBus> }} options
 */
export function mountEnvelopePanel({ root, bus }) {
    const body = el('div', { class: 'envelope' });
    root.append(el('h2', { text: 'The LuiResult' }), body);
    body.append(el('p', { class: 'muted', text: 'Type in one of the autocompletes.' }));

    const fact = (name, value, flag) => el('div', { class: `fact${flag ? ' flag' : ''}` }, el('dt', { text: name }), el('dd', { text: String(value) }));

    function timings(envelope) {
        if (!envelope.timings) {
            return el('p', { class: 'muted', text: 'TrackTimings is off: no breakdown.' });
        }
        const total = Math.max(envelope.timings.totalMs, 0.0001);
        const bar = el('div', { class: 'bar', role: 'img', 'aria-label': 'time per stage' });
        for (const [key, , className] of SEGMENTS) {
            const segment = el('span', { class: `seg ${className}` });
            segment.style.width = `${Math.max((envelope.timings[key] / total) * 100, 0)}%`;
            bar.append(segment);
        }
        return el('div', {}, bar, el('ul', { class: 'legend' },
            ...SEGMENTS.map(([key, label, className]) => el('li', {}, el('i', { class: `seg ${className}` }), `${label} ${ms(envelope.timings[key])}`)),
            el('li', { class: 'strong', text: `total ${ms(envelope.timings.totalMs)}` })));
    }

    bus.on('suggest:response', ({ index, request, envelope, roundtripMs }) => {
        clear(body);
        // flexdatalist adds three parameters of its own to every request; the server ignores them, the page need not show them
        const sent = Object.fromEntries(Object.entries(request).filter(([name]) => !LIBRARY_PARAMETERS.has(name)));
        body.append(
            el('p', { class: 'request' }, el('strong', { text: index }), ' ', el('code', { text: `?${new URLSearchParams(sent)}` })),
            el('dl', { class: 'facts' },
                fact('Query', JSON.stringify(envelope.query)),
                fact('CorrectedQuery', envelope.correctedQuery === undefined ? '–' : JSON.stringify(envelope.correctedQuery), envelope.correctedQuery !== undefined),
                fact('CorrectionSkipped', envelope.correctionSkipped, envelope.correctionSkipped),
                fact('Truncated', envelope.truncated, envelope.truncated),
                fact('CandidatesConsidered', number(envelope.candidatesConsidered)),
                fact('Hits', envelope.results.length),
                fact('Server', ms(envelope.serverMs)),
                fact('Round trip', ms(roundtripMs)),
                fact('Typo dictionary', envelope.typoDictionaryState)),
            timings(envelope),
            el('details', {}, el('summary', { text: 'Raw response' }), el('pre', { text: JSON.stringify(envelope, null, 2) })));
    });
}
